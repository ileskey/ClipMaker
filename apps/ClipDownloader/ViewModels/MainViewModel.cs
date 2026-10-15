using ClipDownloader.Application.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClipDownloader.ViewModels;

public enum MetadataState { Empty, Loading, Ready, Failed }
public enum ClipState { Idle, Preparing, AwaitingConfirmation, Executing, Cancelling, Completed, Failed, Cancelled }
public enum AppLifetime { Running, Closing, Closed }

public sealed partial class MainViewModel : ValidatingViewModel
{
    private readonly IToolDiagnosticsService diagnostics;
    private readonly ILogger<MainViewModel> logger;
    private CancellationTokenSource? activeCancellation;
    private Task activeTask = Task.CompletedTask;
    private Task? shutdownTask;
    private long generation;

    [ObservableProperty] private MetadataState metadataState;
    [ObservableProperty] private ClipState jobState;
    [ObservableProperty] private AppLifetime lifetime;
    [ObservableProperty] private bool toolsSearching;
    [ObservableProperty] private bool toolsInitialized;
    [ObservableProperty] private string initializationStatus = "기본 초기화 완료 · 도구 확인 중";
    [ObservableProperty] private string statusText = "영상 URL을 입력하세요.";
    [ObservableProperty] private IReadOnlyList<ToolDiagnosticItem> toolDiagnostics =
    [new("yt-dlp.exe", "확인 중", ""), new("ffmpeg.exe", "확인 중", ""), new("ffprobe.exe", "확인 중", "")];

    public bool IsBusy => !activeTask.IsCompleted || ToolsSearching || MetadataState == MetadataState.Loading ||
        JobState is ClipState.Preparing or ClipState.Executing or ClipState.Cancelling;
    public bool CanEdit => Lifetime == AppLifetime.Running && !IsBusy && !DialogOpen;
    public IAsyncRelayCommand RefreshToolsCommand { get; }

    public MainViewModel(IToolDiagnosticsService diagnostics, ILogger<MainViewModel> logger,
        IVideoInfoService videoInfo, IVideoSourceParser sourceParser, IOutputPathValidator outputValidator,
        Dialogs.IOutputFileDialog outputDialog, IClipJobService jobs,
        IThumbnailContentService thumbnails, Imaging.IThumbnailDecoder thumbnailDecoder)
    {
        this.diagnostics = diagnostics;
        this.logger = logger;
        this.videoInfo = videoInfo;
        this.sourceParser = sourceParser;
        this.outputValidator = outputValidator;
        this.outputDialog = outputDialog;
        this.jobs = jobs;
        this.thumbnails = thumbnails;
        this.thumbnailDecoder = thumbnailDecoder;
        CancelThumbnailCommand = new RelayCommand(CancelThumbnail, CanCancelThumbnail);
        RefreshToolsCommand = new AsyncRelayCommand(() => StartOperationAsync(RefreshToolsAsync), () => CanEdit);
        InitializeClipCommands();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Lifetime) or nameof(MetadataState) or nameof(JobState) or nameof(ToolsSearching) or nameof(ToolsInitialized))
                NotifyCommands();
        };
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        RefreshToolsCommand.NotifyCanExecuteChanged();
        CancelThumbnailCommand.NotifyCanExecuteChanged();
        NotifyClipCommands();
    }

    partial void NotifyClipCommands();

    private Task StartOperationAsync(Func<CancellationToken, long, Task> operation)
    {
        if (!CanEdit) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new CancellationTokenSource();
        activeCancellation = cancellation;
        activeTask = completion.Task;
        long current = ++generation;
        _ = RunAsync();
        return completion.Task;

        async Task RunAsync()
        {
            try { await operation(cancellation.Token, current); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { StatusText = "취소되었습니다."; }
            catch (Exception)
            {
                logger.LogError("Unexpected operation failure; generation {Generation}", current);
                StatusText = "작업 중 예상하지 못한 오류가 발생했습니다.";
            }
            finally
            {
                activeCancellation = null;
                cancellation.Dispose();
                completion.TrySetResult();
                NotifyCommands();
            }
        }
    }

    private async Task RefreshToolsAsync(CancellationToken token, long current)
    {
        ToolsSearching = true;
        InitializationStatus = "기본 초기화 완료 · 도구 확인 중";
        try
        {
            var results = await Task.Run(() => diagnostics.DiagnoseAsync(token));
            if (Lifetime != AppLifetime.Running || token.IsCancellationRequested || generation != current) return;
            ToolDiagnostics = results.Select(ToolDiagnosticItem.From).ToArray();
            InitializationStatus = "기본 초기화 완료 · 도구 확인 완료 (파일 발견 검사)";
            foreach (var result in results)
                logger.LogInformation("Tool {ToolName}: {Status}", result.ToolName, result.Status);
        }
        finally { ToolsSearching = false; ToolsInitialized = true; }
    }

    public Task ShutdownAsync()
    {
        if (shutdownTask is not null) return shutdownTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shutdownTask = completion.Task;
        Lifetime = AppLifetime.Closing;
        InvalidateThumbnail();
        InvalidateForShutdown();
        StatusText = "종료 요청 · 취소와 정리가 끝날 때까지 기다립니다.";
        if (JobState == ClipState.Executing) JobState = ClipState.Cancelling;
        activeCancellation?.Cancel();
        _ = FinishShutdownAsync();
        return shutdownTask;

        async Task FinishShutdownAsync()
        {
            try { await AwaitShutdownAsync(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }
    }

    partial void InvalidateForShutdown();

    private async Task AwaitShutdownAsync()
    {
        await activeTask;
        await pathTask;
        await thumbnailTask;
        StatusText = RequiresCloseAcknowledgement ? "종료 전 경고와 잔존 자원을 확인하세요." : "작업과 정리가 종료되었습니다.";
        NotifyCommands();
    }

    public void MarkClosed() => Lifetime = AppLifetime.Closed;
}
