using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ToolExecutionDiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ClipDownloader-Diagnostics-" + Guid.NewGuid());
    private const string Encoders = "Encoders:\n V..... = Video\n ------\n V....D libx264 H.264\n V....D libx264rgb RGB\n A..... aac AAC\n";
    private const string Muxers = "Formats:\n D.. = Demuxing supported\n .E. = Muxing supported\n ---\n E mp4 MP4\n E d sdl SDL device\n";
    public ToolExecutionDiagnosticsTests() => Directory.CreateDirectory(root);

    private FakeLocator Locator() => new(request => new(ToolLocationStatus.Found, request.UserSpecifiedPath is not null,
        request.UserSpecifiedPath ?? Path.Combine(root, request.ExecutableName), ToolLocationSource.Path, [], null));
    private ToolExecutionDiagnostics Service(IToolLocator locator, IProcessRunner runner, ToolPathSettings? settings = null) =>
        new(locator, settings ?? new(), runner, root);
    private static ProcessOutput Output(byte[] bytes) => new(bytes, bytes.Length, false, false, true, true);
    private static ProcessResult Exited(string text, int? code = 0) => new(ProcessStatus.Exited, code,
        Output(Encoding.UTF8.GetBytes(text)), Output([]), null, true, true, true, null);
    private static string Version(string name) => name switch
    {
        "yt-dlp.exe" => "2026.08.19\n", "ffmpeg.exe" => "ffmpeg version 8.1-full_build Copyright\n",
        "ffprobe.exe" => "ffprobe version 8.1-full_build Copyright\n", _ => throw new InvalidOperationException()
    };
    private static FakeRunner Runner() => new((request, _) => Task.FromResult(Exited(
        request.Arguments.Contains("-encoders") ? Encoders : request.Arguments.Contains("-muxers") ? Muxers :
            Version(Path.GetFileName(request.ExecutablePath)))));
    private static ToolExecutionReport Report(OperationResult<ToolExecutionReport> result)
    {
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.NotNull(result.Data);
        return result.Data;
    }

    [Theory]
    [InlineData(ToolDiagnosticCommand.Diagnose, 3)]
    [InlineData(ToolDiagnosticCommand.Info, 1)]
    [InlineData(ToolDiagnosticCommand.Clip, 3)]
    public async Task CommandSelectsOnlyRequiredToolsAndSeparatesVersionChecks(ToolDiagnosticCommand command, int expectedCount)
    {
        var locator = Locator();
        var runner = Runner();
        var report = Report(await Service(locator, runner).DiagnoseAsync(new(command), TestContext.Current.CancellationToken));
        Assert.True(report.AllRequestedChecksPassed);
        Assert.Equal(expectedCount, report.Tools.Count);
        Assert.Equal(expectedCount, locator.Requests.Count);
        Assert.Equal(expectedCount, runner.Calls.Count);
        Assert.All(report.Tools, tool => Assert.Equal(ToolDiagnosticStatus.Found, tool.Location.Status));
        Assert.All(runner.Calls, request =>
        {
            Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
            Assert.Equal(ProcessOutputMode.Full, request.StandardOutputMode);
            Assert.Equal(ProcessOutputMode.Tail, request.StandardErrorMode);
        });
        Assert.Equal(new[] { "--ignore-config", "--version" }, runner.Calls[0].Arguments);
        Assert.Equal("2026.08.19", report.Tools[0].Version);
        if (expectedCount == 3) Assert.Equal(new[] { "-version" }, runner.Calls[1].Arguments);
    }

    [Theory]
    [InlineData(ToolLocationStatus.Missing, OperationErrorCode.MissingTool)]
    [InlineData(ToolLocationStatus.SearchFailed, OperationErrorCode.ProcessFailed)]
    public async Task FailedLocationDoesNotPreventOtherToolsBeingDiagnosed(ToolLocationStatus status, OperationErrorCode expected)
    {
        var locator = new FakeLocator(request => request.ExecutableName == "yt-dlp.exe"
            ? new(status, false, null, null, [], "raw token=SECRET") :
            new(ToolLocationStatus.Found, false, Path.Combine(root, request.ExecutableName), ToolLocationSource.Path, [], null));
        var runner = Runner();
        var report = Report(await Service(locator, runner).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), TestContext.Current.CancellationToken));
        Assert.False(report.AllRequestedChecksPassed);
        Assert.Equal(3, report.Tools.Count);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(expected, report.Tools[0].Error!.Code);
        Assert.Equal(OperationErrorCategory.Execution, report.Tools[0].Error!.Category);
        Assert.DoesNotContain("SECRET", report.Tools[0].Location.FailureReason!);
        Assert.True(report.Tools[1].AllRequestedChecksPassed);
        Assert.True(report.Tools[2].AllRequestedChecksPassed);
    }

    [Fact]
    public async Task ExplicitMissingPathNeverFallsBackToFoundToolsOrPath()
    {
        File.WriteAllText(Path.Combine(root, "yt-dlp.exe"), "exists in PATH fixture");
        var locator = new ToolLocator(new(root, [root]));
        var runner = Runner();
        var settings = new ToolPathSettings(new Dictionary<string, string?> { ["yt-dlp.exe"] = "missing.exe" });
        var report = Report(await Service(locator, runner, settings).DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.Equal(ToolDiagnosticStatus.Missing, report.Tools[0].Location.Status);
        Assert.True(report.Tools[0].Location.IsUserSpecified);
        Assert.Single(report.Tools[0].Location.Inspections);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FailedExecutionUsesTheSelectedPathAndDoesNotRetryOrFallback()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(Exited("", 7)));
        string explicitPath = Path.Combine(root, "explicit.exe");
        var settings = new ToolPathSettings(new Dictionary<string, string?> { ["yt-dlp.exe"] = explicitPath });
        var report = Report(await Service(Locator(), runner, settings).DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.False(report.AllRequestedChecksPassed);
        Assert.Equal(ToolCheckStatus.Failed, report.Tools[0].ExecutionStatus);
        Assert.Equal(ToolCheckStatus.NotChecked, report.Tools[0].VersionStatus);
        Assert.Equal(7, report.Tools[0].VersionExitCode);
        Assert.Single(runner.Calls);
        Assert.Equal(explicitPath, runner.Calls[0].ExecutablePath);
    }

    [Theory]
    [InlineData(ProcessStatus.StartFailed, OperationErrorCode.ProcessFailed)]
    [InlineData(ProcessStatus.ExecutionFailed, OperationErrorCode.ProcessFailed)]
    [InlineData(ProcessStatus.TimedOut, OperationErrorCode.TimedOut)]
    public async Task ConfirmedProcessFailuresRemainPerToolAndDiagnoseContinues(ProcessStatus status, OperationErrorCode expected)
    {
        var runner = new FakeRunner((request, _) => Task.FromResult(Path.GetFileName(request.ExecutablePath) == "yt-dlp.exe"
            ? Exited("") with { Status = status, ExitCode = null, Failure = new(ProcessFailureKind.OutputReadFailed, "raw SECRET") }
            : Exited(Version(Path.GetFileName(request.ExecutablePath)))));
        var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), TestContext.Current.CancellationToken));
        Assert.Equal(expected, report.Tools[0].Error!.Code);
        Assert.DoesNotContain("SECRET", report.Tools[0].Error!.Message);
        Assert.Equal(3, runner.Calls.Count);
        Assert.False(report.AllRequestedChecksPassed);
    }

    [Fact]
    public async Task ExitCodeZeroWithUnrecognizedVersionIsNotSuccessfulVersionVerification()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(Exited("secret=https://signed.invalid/token\n")));
        var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.Equal(ToolCheckStatus.Passed, report.Tools[0].ExecutionStatus);
        Assert.Equal(ToolCheckStatus.Failed, report.Tools[0].VersionStatus);
        Assert.Null(report.Tools[0].Version);
        Assert.DoesNotContain("signed.invalid", report.Tools[0].Error!.Message);
    }

    [Theory]
    [InlineData("2026.08.19", true)]
    [InlineData("2026.08.19.232125", true)]
    [InlineData("\uFEFF2026.08.19\r\n", true)]
    [InlineData("2026.13.01", false)]
    [InlineData("2026.02.30", false)]
    [InlineData("not a version", false)]
    public async Task YtDlpVersionRequiresValidDateWithoutInterpretingArbitraryOutput(string text, bool expected)
    {
        var report = Report(await Service(Locator(), new FakeRunner((_, _) => Task.FromResult(Exited(text))))
            .DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.Equal(expected, report.AllRequestedChecksPassed);
    }

    [Fact]
    public async Task TruncatedAndInvalidUtf8ResponsesCannotBeTreatedAsCompleteVersions()
    {
        foreach (var output in new[]
        {
            Output(Encoding.UTF8.GetBytes("2026.08.19")) with { IsTruncated = true },
            Output(Encoding.UTF8.GetBytes("2026.08.19")) with { IsComplete = false },
            Output(Encoding.UTF8.GetBytes("2026.08.19")) with { LimitExceeded = true },
            Output(new byte[] { 0xff })
        })
        {
            var runner = new FakeRunner((_, _) => Task.FromResult(Exited("") with { StandardOutput = output }));
            var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
            Assert.False(report.AllRequestedChecksPassed);
            Assert.Null(report.Tools[0].Version);
        }
    }

    [Fact]
    public async Task ClipFeaturesAreCheckedOncePerListingKindWithExactIdentifiers()
    {
        ToolFeatureRequirement[] features = [new(ToolFeatureKind.Encoder, "libx264"), new(ToolFeatureKind.Encoder, "aac"),
            new(ToolFeatureKind.Muxer, "mp4"), new(ToolFeatureKind.Encoder, "libx264")];
        var runner = Runner();
        var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Clip, features), TestContext.Current.CancellationToken));
        Assert.True(report.AllRequestedChecksPassed);
        Assert.Equal(5, runner.Calls.Count);
        Assert.Equal(3, report.Tools[1].Features.Count);
        Assert.Equal(1, runner.Calls.Count(call => call.Arguments.Contains("-encoders")));
        Assert.Equal(1, runner.Calls.Count(call => call.Arguments.Contains("-muxers")));
        Assert.All(report.Tools[1].Features, feature => Assert.Equal(ToolCheckStatus.Passed, feature.Status));
    }

    [Fact]
    public async Task SimilarCodecNameDoesNotSatisfyRequestedEncoderAndOtherToolsStillRun()
    {
        var runner = new FakeRunner((request, _) => Task.FromResult(Exited(request.Arguments.Contains("-encoders")
            ? "Encoders:\n V....D libx264rgb RGB\n" : Version(Path.GetFileName(request.ExecutablePath)))));
        var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Clip,
            [new(ToolFeatureKind.Encoder, "libx264")]), TestContext.Current.CancellationToken));
        Assert.False(report.AllRequestedChecksPassed);
        Assert.Equal(ToolCheckStatus.Passed, report.Tools[1].VersionStatus);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, report.Tools[1].Features[0].Error!.Code);
        Assert.True(report.Tools[2].AllRequestedChecksPassed);
    }

    [Theory]
    [InlineData("File formats:\n DE mov,mp4,m4a combined\n", true)]
    [InlineData("Formats:\n E mp4 MP4\n", true)]
    [InlineData("Formats:\n D mp4 demux only\n E mov other\n", false)]
    [InlineData("This is not a feature table\n E mp4 MP4\n", false)]
    public async Task MuxerParsingRequiresRecognizedHeaderAndEncodingFlag(string listing, bool expected)
    {
        var runner = new FakeRunner((request, _) => Task.FromResult(Exited(request.Arguments.Contains("-muxers")
            ? listing : Version(Path.GetFileName(request.ExecutablePath)))));
        var report = Report(await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Clip,
            [new(ToolFeatureKind.Muxer, "mp4")]), TestContext.Current.CancellationToken));
        Assert.Equal(expected, report.AllRequestedChecksPassed);
    }

    [Fact]
    public async Task UnknownExitCodeDoesNotBecomeSuccess()
    {
        var report = Report(await Service(Locator(), new FakeRunner((_, _) => Task.FromResult(Exited("2026.08.19", null))))
            .DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.Equal(OperationErrorCode.ProcessFailed, report.Tools[0].Error!.Code);
        Assert.Null(report.Tools[0].VersionExitCode);
    }

    [Theory]
    [InlineData(ProcessStatus.TerminationFailed)]
    [InlineData(ProcessStatus.Exited)]
    public async Task UnconfirmedTerminationStopsAllFurtherProbes(ProcessStatus status)
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(Exited("2026.08.19") with
        { Status = status, DescendantExitConfirmed = false }));
        var result = await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task ConfirmedCancellationAndAlreadyCancelledInputAreSeparateFromErrors()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(Exited("") with { Status = ProcessStatus.Cancelled }));
        var result = await Service(Locator(), runner).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Cancelled, result.Status);
        Assert.Null(result.Error);
        Assert.Single(runner.Calls);
        var locator = Locator();
        result = await Service(locator, Runner()).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), new CancellationToken(true));
        Assert.Equal(OperationStatus.Cancelled, result.Status);
        Assert.Empty(locator.Requests);
    }

    [Fact]
    public async Task CancellationAfterLocationDoesNotEnterProcessStage()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var locator = new FakeLocator(request =>
        {
            cancellation.Cancel();
            return new(ToolLocationStatus.Found, false, Path.Combine(root, request.ExecutableName), ToolLocationSource.Path, [], null);
        });
        var runner = Runner();
        var result = await Service(locator, runner).DiagnoseAsync(new(ToolDiagnosticCommand.Info), cancellation.Token);
        Assert.Equal(OperationStatus.Cancelled, result.Status);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ReportIsRecheckedOnNextCallInsteadOfCachingStaleVersion()
    {
        int invocation = 0;
        var runner = new FakeRunner((_, _) => Task.FromResult(Exited(++invocation == 1 ? "2026.08.19" : "2026.09.01")));
        var service = Service(Locator(), runner);
        var first = Report(await service.DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        var second = Report(await service.DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
        Assert.Equal("2026.08.19", first.Tools[0].Version);
        Assert.Equal("2026.09.01", second.Tools[0].Version);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ProgrammingErrorsAreNotHiddenAsGenericToolFailures()
    {
        var runner = new FakeRunner((_, _) => throw new InvalidOperationException("programming fixture"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Locator(), runner)
            .DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingWpfDiagnosticsStillMeansFilePresenceWithoutExecutingTools()
    {
        foreach (string name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            File.WriteAllText(Path.Combine(root, name), "This file cannot execute.");
        var original = new ToolDiagnosticsService(new ToolLocator(new(root, [root])), new());
        var results = await original.DiagnoseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, results.Count);
        Assert.All(results, tool => Assert.Equal(ToolDiagnosticStatus.Found, tool.Status));
    }

    private sealed class FakeLocator(Func<ToolSearchRequest, ToolLocationResult> locate) : IToolLocator
    {
        public List<ToolSearchRequest> Requests { get; } = [];
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return locate(request);
        }
    }
    private sealed class FakeRunner(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> run) : IProcessRunner
    {
        public List<ProcessRequest> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken)
        {
            Assert.Null(outputReceived);
            Calls.Add(request);
            return run(request, cancellationToken);
        }
    }
    public void Dispose() => Directory.Delete(root, recursive: true);
}
