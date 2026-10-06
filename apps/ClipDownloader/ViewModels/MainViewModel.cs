using System.Diagnostics;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClipDownloader.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IToolDiagnosticsService diagnostics;
    private readonly ILogger<MainViewModel> logger;
    private CancellationTokenSource? activeSearch;
    private bool stopped;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string initializationStatus = "기본 초기화 완료 · 도구 확인 중";

    [ObservableProperty]
    private IReadOnlyList<ToolDiagnosticItem> toolDiagnostics = new[]
    {
        new ToolDiagnosticItem("yt-dlp.exe", "확인 중", ""),
        new ToolDiagnosticItem("ffmpeg.exe", "확인 중", ""),
        new ToolDiagnosticItem("ffprobe.exe", "확인 중", "")
    };

    public IAsyncRelayCommand RefreshToolsCommand { get; }

    public MainViewModel(IToolDiagnosticsService diagnostics, ILogger<MainViewModel> logger)
    {
        this.diagnostics = diagnostics;
        this.logger = logger;
        RefreshToolsCommand = new AsyncRelayCommand(RefreshToolsAsync, () => !stopped && !IsBusy);
    }

    private async Task RefreshToolsAsync()
    {
        if (stopped || IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        activeSearch = cancellation;
        IsBusy = true;
        RefreshToolsCommand.NotifyCanExecuteChanged();
        InitializationStatus = "기본 초기화 완료 · 도구 확인 중";
        try
        {
            var results = await diagnostics.DiagnoseAsync(cancellation.Token);
            if (stopped) return;
            ToolDiagnostics = results.Select(ToolDiagnosticItem.From).ToArray();
            InitializationStatus = "기본 초기화 완료 · 도구 확인 완료";
            foreach (var result in results)
            {
                logger.LogInformation("Tool {ToolName}: {Status}; user specified: {IsUserSpecified}; path: {Path}; source: {Source}; reason: {Reason}",
                    result.ToolName, result.Status, result.IsUserSpecified, result.SelectedPath,
                    result.Source, result.FailureReason);
                foreach (var item in result.Inspections)
                    logger.LogInformation("Candidate {Path} ({Source}): {Status}; {Reason}",
                        item.Path, item.Source, item.Status, item.FailureReason);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Shutdown cancellation is not a tool failure.
        }
        catch (Exception ex)
        {
            if (stopped) Debug.WriteLine(ex);
            else
            {
                logger.LogError(ex, "Tool diagnostics failed");
                InitializationStatus = "도구 확인 중 문제가 발생했습니다. 다시 확인할 수 있습니다.";
            }
        }
        finally
        {
            activeSearch = null;
            if (!stopped)
            {
                IsBusy = false;
                RefreshToolsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // Called on the UI thread before disposing DI and logging.
    public void Stop()
    {
        if (stopped) return;
        stopped = true;
        RefreshToolsCommand.NotifyCanExecuteChanged();
        activeSearch?.Cancel();
    }
}
