using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ToolDiagnosticsValidationTests
{
    private static readonly string Root = Path.GetTempPath();
    private static ProcessOutput Output(string text) => new(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text), false, false, true, true);
    private static ProcessResult Result(string text) => new(ProcessStatus.Exited, 0, Output(text), Output(""), null, true, true, true, null);
    private sealed class Locator : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken) =>
            new(ToolLocationStatus.Found, false, Path.Combine(Root, request.ExecutableName), ToolLocationSource.Path, [], null);
    }
    private sealed class Runner(Func<ProcessRequest, ProcessResult> probe) : IProcessRunner
    {
        public List<ProcessRequest> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(probe(request));
        }
    }
    private static ToolExecutionDiagnostics Service(Runner runner) => new(new Locator(), new(), runner, Root);

    [Theory]
    [InlineData("ffmpeg version N-12345-gabcdef Copyright", "ffprobe version 8.1 Copyright", true)]
    [InlineData("ffmpeg version 8.1 Copyright", "ffmpeg version 8.1 Copyright", false)]
    [InlineData("ffmpeg version unknown Copyright", "ffprobe version 8.1 Copyright", false)]
    public async Task FfmpegAndFfprobeRequireTheirOwnIdentifyingVersionHeader(string ffmpeg, string ffprobe, bool expected)
    {
        var runner = new Runner(request => Result(Path.GetFileName(request.ExecutablePath) switch
        {
            "yt-dlp.exe" => "2026.08.19", "ffmpeg.exe" => ffmpeg, _ => ffprobe
        }));
        var result = await Service(runner).DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Data!.AllRequestedChecksPassed);
    }

    [Fact]
    public async Task ExcessivelyLongYtDlpVersionIsNotExposedAsVerifiedMetadata()
    {
        var runner = new Runner(_ => Result("2026.08.19." + new string('a', 1000)));
        var result = await Service(runner).DiagnoseAsync(new(ToolDiagnosticCommand.Info), TestContext.Current.CancellationToken);
        Assert.Equal(ToolCheckStatus.Failed, result.Data!.Tools[0].VersionStatus);
        Assert.Null(result.Data.Tools[0].Version);
    }

    [Theory]
    [InlineData(ProcessStatus.Cancelled, OperationStatus.Cancelled)]
    [InlineData(ProcessStatus.TerminationFailed, OperationStatus.Failed)]
    public async Task InterruptedFeatureQueryStopsBeforeRemainingTools(ProcessStatus status, OperationStatus expected)
    {
        var runner = new Runner(request => request.Arguments.Contains("-encoders")
            ? Result("") with { Status = status } : Result(Path.GetFileName(request.ExecutablePath) == "yt-dlp.exe"
                ? "2026.08.19" : "ffmpeg version 8.1 Copyright"));
        var result = await Service(runner).DiagnoseAsync(new(ToolDiagnosticCommand.Clip,
            [new(ToolFeatureKind.Encoder, "libx264")]), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Status);
        Assert.Equal(3, runner.Calls.Count);
        Assert.DoesNotContain(runner.Calls, request => Path.GetFileName(request.ExecutablePath) == "ffprobe.exe");
    }

    [Fact]
    public async Task ListingOverflowDoesNotAcceptAFeatureInThePartialResponse()
    {
        var runner = new Runner(request => request.Arguments.Contains("-encoders")
            ? Result("Encoders:\n V....D libx264 H.264") with { StandardOutput = Output("Encoders:\n V....D libx264 H.264") with { LimitExceeded = true } }
            : Result(Path.GetFileName(request.ExecutablePath) switch
            { "yt-dlp.exe" => "2026.08.19", "ffmpeg.exe" => "ffmpeg version 8.1 Copyright", _ => "ffprobe version 8.1 Copyright" }));
        var result = await Service(runner).DiagnoseAsync(new(ToolDiagnosticCommand.Clip,
            [new(ToolFeatureKind.Encoder, "libx264")]), TestContext.Current.CancellationToken);
        Assert.False(result.Data!.AllRequestedChecksPassed);
        Assert.Equal(OperationErrorCode.ProcessFailed, result.Data.Tools[1].Features[0].Error!.Code);
        Assert.Equal(ToolCheckStatus.Passed, result.Data.Tools[2].VersionStatus);
    }
}
