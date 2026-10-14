using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.UseCases;
using ClipDownloader.Cli;
using ClipDownloader.Cli.Composition;
using ClipDownloader.Infrastructure.Downloads;
using ClipDownloader.Infrastructure.FileSystem;
using ClipDownloader.Infrastructure.Sources;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class CliTests
{
    private sealed class ConsoleStub(params string?[] lines) : ICliConsole
    {
        public bool InputRedirected { get; set; }
        public bool ErrorRedirected { get; set; } = true;
        public string Stdout = "", Stderr = "";
        public int Reads;
        public Action? BeforeRead;
        public void Out(string text) => Stdout += text + "\n";
        public void Error(string text) => Stderr += text;
        public Task<string?> ReadLineAsync(CancellationToken token)
        { BeforeRead?.Invoke(); token.ThrowIfCancellationRequested(); return Task.FromResult(Reads < lines.Length ? lines[Reads++] : null); }
    }
    private sealed class Diagnostics : IToolExecutionDiagnostics
    {
        public OperationError? Error;
        public Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(ToolDiagnosticRequest request, CancellationToken token) => Task.FromResult(
            Error is not null ? OperationResult<ToolExecutionReport>.Failed(Error) : OperationResult<ToolExecutionReport>.Succeeded(new(request.Command,
                (request.Command == ToolDiagnosticCommand.Info ? new[] { ToolKind.YtDlp } : Enum.GetValues<ToolKind>()).Select(kind => new ToolExecutionDiagnostic(kind,
                new(kind.ToString(), ToolDiagnosticStatus.Found, false, "fixture", ToolDiagnosticSource.Path, [], null), ToolCheckStatus.Passed,
                ToolCheckStatus.Passed, "fixture", 0, null, kind == ToolKind.Ffmpeg ? request.RequiredFeatures.Select(f => new ToolFeatureCheck(f, ToolCheckStatus.Passed, null)) : [])))));
    }
    private sealed class Metadata : IVideoInfoService
    {
        public VideoInfo Info = new(F.Snapshot(Guid.NewGuid()).Source, "fixture\u001b\nTitle", TimeSpan.FromSeconds(120), "uploader", null, null,
            VideoState.Completed, [F.Snapshot(Guid.NewGuid()).Selection.VideoFormat], []);
        public OperationError? Error;
        public Task<OperationResult<VideoInfo>> GetVideoInfoAsync(Uri url, CancellationToken token) => Task.FromResult(Error is null ?
            OperationResult<VideoInfo>.Succeeded(Info) : OperationResult<VideoInfo>.Failed(Error));
    }
    private sealed class Media : IClipMediaService
    {
        public ClipMediaResources GetOwnedResources(Guid jobId) => new([], false);
        public Task<OperationResult<VerifiedClipOutput>> ProcessAsync(AcquiredMedia acquired, Guid stagingFileId, string stagingPath,
            IProgress<JobProgress>? progress, CancellationToken token) => throw new InvalidOperationException("No media in CLI fixture.");
    }
    private sealed class Jobs(IClipJobService preparation) : IClipJobService
    {
        public int Executions;
        public Func<PreparedClip, CancellationToken, ClipJobResult>? Result;
        public Task<OperationResult<PreparedClip>> PrepareAsync(ClipRequest request, VideoInfo info, CancellationToken token) => preparation.PrepareAsync(request, info, token);
        public Task<ClipJobResult> ExecuteAsync(PreparedClip prepared, IProgress<JobProgress>? progress, CancellationToken token)
        {
            Executions++;
            progress?.Report(new(prepared.JobId, JobStage.Finalizing, 100, "fixture"));
            return Task.FromResult(Result?.Invoke(prepared, token) ?? new ClipJobResult(prepared.JobId, OperationStatus.Succeeded, prepared.FinalPath, null, [], [],
                new(F.Range(1, 2), F.Range(0, 3), F.Range(0, 3), false, 2, 1, 1024, 256)));
        }
    }
    private sealed class Harness : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "clip-cli", Guid.NewGuid().ToString("N"));
        public readonly Metadata Metadata = new();
        public readonly Diagnostics Diagnostics = new();
        public readonly SelectedSourceAcquisitionService Source;
        public readonly Jobs Jobs;
        public readonly OutputPathValidator Paths;
        public Harness()
        {
            Directory.CreateDirectory(DirectoryPath);
            var locator = new ToolLocator(new(DirectoryPath, [])); var settings = new ToolPathSettings(); var runner = new ProcessRunner();
            Source = new(Diagnostics, locator, settings, runner, DirectoryPath); Paths = new(DirectoryPath);
            Jobs = new(new ClipJobService(Metadata, new SupportedVideoSourceParser(), Diagnostics, Paths, new OutputWorkspace(Paths, Path.Combine(DirectoryPath, "work")),
                Source, Source, Source, new Media(), new(32 * 1024 * 1024, F.Time(30))));
        }
        public CliCommand Command(bool yes = false) => new("clip", Metadata.Info.Source.PageUrl, "00:00:01:00", "00:00:02:00", Path.Combine(DirectoryPath, "clip.mp4"), QualityOption.Best, ClipMode.Fast, yes, new Dictionary<string, string>());
        public CliApplication App(ConsoleStub console) => new(Diagnostics, Metadata, Jobs, Paths, console);
        public void Dispose() { Source.Dispose(); Directory.Delete(DirectoryPath); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("info")]
    [InlineData("diagnose --yes")]
    [InlineData("info --url https://example.com/x")]
    [InlineData("info --url https://chzzk.naver.com/video/123 --url x")]
    [InlineData("clip --url https://chzzk.naver.com/video/123 --start 00:00:01:00 --end 00:00:02:00 --output clip.mp4 --mode 0")]
    [InlineData("clip --url https://chzzk.naver.com/video/123 --start 00:99:01:00 --end 00:00:02:00 --output clip.mp4")]
    [InlineData("clip --url https://chzzk.naver.com/video/123 --start 00:00:01:00 --end 00:00:02:00 --output clip.mp4 --quality 0p")]
    [InlineData("clip --url https://chzzk.naver.com/video/123 --start 00:00:01:00 --end 00:00:02:00 --output clip.mp4 --yes --yes")]
    public void RejectsInvalidOptions(string text) => Assert.NotNull(CommandLineParser.Parse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Error);

    [Fact]
    public void DefaultsAndExplicitToolsArePreserved()
    {
        var parsed = CommandLineParser.Parse(["clip", "--url", "https://chzzk.naver.com/video/123?utm=x", "--start", "00:00:01:00", "--end", "00:00:02:00", "--output", "a b.mp4", "--ffmpeg-path", "a b.exe"]);
        Assert.Null(parsed.Error); Assert.Equal(ClipMode.Fast, parsed.Command!.Mode); Assert.Equal(QualityOption.Best, parsed.Command.Quality);
        Assert.Equal("a b.exe", parsed.Command.ToolPaths["ffmpeg.exe"]); Assert.Equal("https://chzzk.naver.com/video/123", parsed.Command.Url!.AbsoluteUri);
        Assert.True(CommandLineParser.Parse(["clip", "--help"]).Help);
    }

    [Theory]
    [InlineData("n")]
    [InlineData(" N ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task NegativeEmptyAndEofNeverExecute(string? answer)
    {
        using var h = new Harness(); var console = new ConsoleStub(answer);
        Assert.Equal(130, await h.App(console).RunAsync(h.Command(), CancellationToken.None));
        Assert.Equal(0, h.Jobs.Executions); Assert.Empty(Directory.GetFileSystemEntries(h.DirectoryPath));
        Assert.Contains("fixtureTitle", console.Stdout); Assert.DoesNotContain('\u001b', console.Stdout);
    }
    [Fact]
    public async Task InvalidAnswerRetriesThenYesExecutesWithActualRanges()
    {
        using var h = new Harness(); var console = new ConsoleStub("what", " y ");
        Assert.Equal(0, await h.App(console).RunAsync(h.Command(), CancellationToken.None));
        Assert.Equal(2, console.Reads); Assert.Equal(1, h.Jobs.Executions);
        Assert.Contains("요청 범위: [1, 2)", console.Stdout); Assert.Contains("실제 영상 표시 범위: [0, 3)", console.Stdout);
    }
    [Fact]
    public async Task RedirectedInputRequiresYesButYesStillValidates()
    {
        using var h = new Harness(); var console = new ConsoleStub { InputRedirected = true };
        Assert.Equal(2, await h.App(console).RunAsync(h.Command(), CancellationToken.None)); Assert.Equal(0, console.Reads); Assert.Equal(0, h.Jobs.Executions);
        Assert.Equal(0, await h.App(console).RunAsync(h.Command(true), CancellationToken.None)); Assert.Equal(0, console.Reads); Assert.Equal(1, h.Jobs.Executions);
        Assert.Equal(2, await h.App(console).RunAsync(h.Command(true) with { Start = "bad" }, CancellationToken.None)); Assert.Equal(1, h.Jobs.Executions);
    }
    [Fact]
    public async Task CancellationWhileConfirmingDoesNotExecute()
    {
        using var h = new Harness(); using var stop = new CancellationTokenSource(); var console = new ConsoleStub("y") { BeforeRead = stop.Cancel };
        Assert.Equal(130, await h.App(console).RunAsync(h.Command(), stop.Token)); Assert.Equal(0, h.Jobs.Executions);
    }
    [Fact]
    public async Task WarningAndRemainingPathDoNotReplaceSuccess()
    {
        using var h = new Harness(); var console = new ConsoleStub();
        h.Jobs.Result = (p, _) => new(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, ["cleanup warning"], ["remaining"]);
        Assert.Equal(0, await h.App(console).RunAsync(h.Command(true), CancellationToken.None));
        Assert.Contains("cleanup warning", console.Stderr); Assert.Contains("remaining", console.Stderr);
    }
    [Theory]
    [InlineData(OperationErrorCategory.Input, 2)]
    [InlineData(OperationErrorCategory.Execution, 1)]
    public async Task MapsStructuredErrorCategories(OperationErrorCategory category, int expected)
    {
        using var h = new Harness(); h.Metadata.Error = new(OperationErrorCode.InvalidMetadata, category, "fixture");
        Assert.Equal(expected, await h.App(new()).RunAsync(h.Command(true), CancellationToken.None)); Assert.Equal(0, h.Jobs.Executions);
    }
    [Fact]
    public async Task DiagnoseFailureAndCancelledExecutionMapCorrectly()
    {
        using var h = new Harness(); h.Diagnostics.Error = new(OperationErrorCode.MissingTool, OperationErrorCategory.Execution, "missing");
        Assert.Equal(1, await h.App(new()).RunAsync(h.Command() with { Name = "diagnose" }, CancellationToken.None));
        h.Diagnostics.Error = null; h.Jobs.Result = (p, _) => new(p.JobId, OperationStatus.Cancelled, null, null, ["warning"], ["remaining"]);
        Assert.Equal(130, await h.App(new()).RunAsync(h.Command(true), CancellationToken.None));
    }
    [Fact]
    public void RedirectedProgressWritesOnlyTransitionsAndTerminalUsesCarriageReturn()
    {
        var console = new ConsoleStub(); var progress = new ConsoleProgress(console);
        for (int i = 0; i < 10; i++) progress.Report(new(Guid.NewGuid(), JobStage.Downloading, i, "raw secret"));
        progress.Report(new(Guid.NewGuid(), JobStage.Processing, null, "raw secret")); progress.Finish();
        Assert.Equal("[Downloading] 진행 중\n[Processing] 진행 중\n", console.Stderr);
        console.ErrorRedirected = false; progress.Report(new(Guid.NewGuid(), JobStage.Processing, 50, "raw secret")); progress.Finish();
        Assert.Contains("\r[Processing] 50.0%", console.Stderr); Assert.DoesNotContain("raw secret", console.Stderr);
    }
    [Fact]
    public void CompositionBuildsWithoutWpfAndPreservesSourceOwnerIdentity()
    {
        using var h = new Harness(); using var services = ServiceRegistration.Create(h.Command(), new ConsoleStub(), null);
        Assert.Same(services.GetRequiredService<SelectedSourceAcquisitionService>(), services.GetRequiredService<SelectedSourceAcquisitionService>());
        Assert.NotNull(services.GetRequiredService<CliApplication>());
        Assert.DoesNotContain(typeof(CliApplication).Assembly.GetReferencedAssemblies(), a => a.Name == "ClipDownloader");
    }
}
