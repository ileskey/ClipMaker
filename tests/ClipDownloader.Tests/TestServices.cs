using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.UseCases;
using ClipDownloader.Dialogs;
using ClipDownloader.Infrastructure.FileSystem;
using ClipDownloader.Infrastructure.Sources;
using ClipDownloader.ViewModels;
using ClipMaker.Media.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClipDownloader.Tests;

internal sealed class TestServices
{
    public VideoInfo Info { get; set; } = Video();
    public Func<Uri, CancellationToken, Task<OperationResult<VideoInfo>>>? Load { get; set; }
    public FakeJobs Jobs { get; }
    public FakeDialog Dialog { get; } = new();
    public MainViewModel Vm { get; }
    public TestServices(IToolDiagnosticsService? diagnostics = null, IOutputPathValidator? validator = null,
        IThumbnailContentService? thumbnails = null, Imaging.IThumbnailDecoder? decoder = null)
    {
        var metadata = new Metadata(this);
        var paths = validator ?? new OutputPathValidator(Environment.CurrentDirectory);
        SignedMediaTime.TryCreate(60, 1, out var span, out _);
        var pipeline = new UnusedPipeline();
        var service = new ClipJobService(metadata, new SupportedVideoSourceParser(), new Diagnostics(), paths,
            new OutputWorkspace(paths), pipeline, pipeline, pipeline, pipeline, new(64 * 1024 * 1024, span!));
        Jobs = new(service);
        Vm = new(diagnostics ?? new ToolDiagnostics(), NullLogger<MainViewModel>.Instance, metadata,
            new SupportedVideoSourceParser(), paths, Dialog, Jobs, thumbnails ?? new NoThumbnails(), decoder ?? new Imaging.WpfThumbnailDecoder());
    }

    public async Task ReadyAsync()
    {
        await Vm.RefreshToolsCommand.ExecuteAsync(null);
        Vm.Url = Info.Source.PageUrl.ToString();
        await Vm.LoadMetadataCommand.ExecuteAsync(null);
        Vm.End.Seconds = "10";
        Vm.OutputPath = Path.Combine(Environment.CurrentDirectory, "phase2-" + Guid.NewGuid() + ".mp4");
        await WaitPathAsync();
    }
    public async Task WaitPathAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Vm.PathChecking) await Task.Delay(10, timeout.Token);
    }
    public static VideoInfo Video(FrameRate? rate = null, bool unknown = false)
    {
        VideoSourceReference.TryCreate(VideoPlatform.YouTube, "qnjEE-wkw8k", out var source);
        FrameRate.TryCreate(30000, 1001, out var defaultRate, out _);
        return new(source!, "A/B", TimeSpan.FromHours(30), "Channel", null, null, VideoState.Completed,
            [new("v", 1280, 720, unknown ? null : rate ?? defaultRate, "h264", "aac", StreamPresence.Present, StreamPresence.Present)], []);
    }
    private sealed class Metadata(TestServices owner) : IVideoInfoService
    {
        public Task<OperationResult<VideoInfo>> GetVideoInfoAsync(Uri uri, CancellationToken token) =>
            owner.Load?.Invoke(uri, token) ?? Task.FromResult(OperationResult<VideoInfo>.Succeeded(owner.Info));
    }
    private sealed class NoThumbnails : IThumbnailContentService
    {
        public Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken token) =>
            Task.FromResult(ThumbnailContentResult.Failed(ThumbnailFailure.HttpFailure));
    }
    private sealed class ToolDiagnostics : IToolDiagnosticsService
    {
        public Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ToolDiagnostic>>([]);
    }
    private sealed class Diagnostics : IToolExecutionDiagnostics
    {
        public Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(ToolDiagnosticRequest request, CancellationToken token) => Task.FromResult(
            OperationResult<ToolExecutionReport>.Succeeded(new(request.Command, Enum.GetValues<ToolKind>().Select(kind => new ToolExecutionDiagnostic(kind,
                new(kind.ToString(), ToolDiagnosticStatus.Found, false, "local", ToolDiagnosticSource.Path, [], null), ToolCheckStatus.Passed,
                ToolCheckStatus.Passed, "fixture", 0, null, kind == ToolKind.Ffmpeg ? request.RequiredFeatures.Select(f => new ToolFeatureCheck(f, ToolCheckStatus.Passed, null)) : [])))));
    }
    internal sealed class FakeDialog : IOutputFileDialog
    {
        public string? Next { get; set; }
        public string? ShowSaveFileDialog(string suggestedName, string currentPath) => Next;
    }
    internal sealed class FakeJobs(IClipJobService inner) : IClipJobService
    {
        public int PrepareCalls, ExecuteCalls;
        public PreparedClip? Prepared, Executed;
        public Func<PreparedClip, IProgress<JobProgress>?, CancellationToken, Task<ClipJobResult>>? Execute { get; set; }
        public Func<ClipRequest, VideoInfo, CancellationToken, Task<OperationResult<PreparedClip>>>? Prepare { get; set; }
        public async Task<OperationResult<PreparedClip>> PrepareAsync(ClipRequest request, VideoInfo info, CancellationToken token)
        { PrepareCalls++; var result = await (Prepare?.Invoke(request, info, token) ?? inner.PrepareAsync(request, info, token)); Prepared = result.Data; return result; }
        public Task<ClipJobResult> ExecuteAsync(PreparedClip value, IProgress<JobProgress>? progress, CancellationToken token)
        { ExecuteCalls++; Executed = value; return Execute?.Invoke(value, progress, token) ?? Task.FromResult(new ClipJobResult(value.JobId, OperationStatus.Succeeded, value.FinalPath, null, [], [])); }
    }
    private sealed class UnusedPipeline : ISelectedSourceInspectionService, IClipDownloadService, ISourceResourceAccess, IClipMediaService
    {
        public Task<OperationResult<SelectedSourceInspection>> InspectAsync(SelectedSourceInspectionRequest request, CancellationToken token) => throw new InvalidOperationException();
        public Task<OperationResult<AcquiredMedia>> AcquireAsync(AcquisitionRequest request, IProgress<AcquisitionProgress>? progress, CancellationToken token) => throw new InvalidOperationException();
        public SourceOwnedResources GetOwnedResources(Guid id) => throw new InvalidOperationException();
        ClipMediaResources IClipMediaService.GetOwnedResources(Guid id) => throw new InvalidOperationException();
        public Task<OperationResult<VerifiedClipOutput>> ProcessAsync(AcquiredMedia media, Guid id, string path, IProgress<JobProgress>? progress, CancellationToken token) => throw new InvalidOperationException();
    }
}
