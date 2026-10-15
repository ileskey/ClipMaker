using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClipDownloader.ViewModels;

public sealed partial class MainViewModel
{
    private PreparedClip? prepared;
    private long preparedInputRevision;
    private long preparedMetadataRevision;
    private bool preparedConsumed;
    private bool acceptingProgress;
    private bool cancellationLocked;
    [ObservableProperty] private bool sessionLocked;
    [ObservableProperty] private string confirmationText = "";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string operationDetails = "";
    [ObservableProperty] private string resourceWarnings = "";
    private readonly HashSet<string> unresolvedResources = new(StringComparer.Ordinal);
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private bool progressIndeterminate = true;
    [ObservableProperty] private bool requiresCloseAcknowledgement;
    public ClipJobResult? LastResult { get; private set; }
    public bool ShowingConfirmation => JobState == ClipState.AwaitingConfirmation && prepared is not null;
    public IAsyncRelayCommand PrepareCommand { get; private set; } = null!;
    public IAsyncRelayCommand ConfirmExecuteCommand { get; private set; } = null!;
    public IRelayCommand BackCommand { get; private set; } = null!;
    public bool CanAcknowledgeClose => Lifetime == AppLifetime.Closing && activeTask.IsCompleted && pathTask.IsCompleted && thumbnailTask.IsCompleted && RequiresCloseAcknowledgement;

    private bool CanPrepare => CanLoad && InputsValid && !SessionLocked && JobState != ClipState.AwaitingConfirmation;
    private bool CanConfirm => CanEdit && ShowingConfirmation && !SessionLocked && !preparedConsumed &&
        preparedInputRevision == inputRevision && preparedMetadataRevision == metadataRevision;

    private void InitializeJobCommands()
    {
        PrepareCommand = new AsyncRelayCommand(() => CanPrepare ? StartOperationAsync(PrepareAsync) : Task.CompletedTask, () => CanPrepare);
        ConfirmExecuteCommand = new AsyncRelayCommand(() => CanConfirm ? StartOperationAsync(ExecuteAsync) : Task.CompletedTask, () => CanConfirm);
        BackCommand = new RelayCommand(() => { if (CanEdit && ShowingConfirmation) InvalidatePrepared(); }, () => CanEdit && ShowingConfirmation);
        SetErrors(nameof(OutputPath), ["MP4 출력 절대 경로를 지정하세요."]);
    }

    partial void NotifyClipCommands()
    {
        LoadMetadataCommand?.NotifyCanExecuteChanged(); BrowseOutputCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged(); PrepareCommand?.NotifyCanExecuteChanged();
        ConfirmExecuteCommand?.NotifyCanExecuteChanged(); BackCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowingConfirmation)); OnPropertyChanged(nameof(CanAcknowledgeClose));
    }

    partial void OnSessionLockedChanged(bool value) => NotifyCommands();
    partial void OnRequiresCloseAcknowledgementChanged(bool value) => NotifyCommands();

    private void InvalidatePrepared()
    {
        prepared = null; ConfirmationText = "";
        if (JobState == ClipState.AwaitingConfirmation)
        { JobState = ClipState.Idle; StatusText = "준비 확인이 해제되었습니다. 다시 다운로드를 눌러 준비하세요."; }
        OnPropertyChanged(nameof(ShowingConfirmation));
    }

    partial void InvalidateForShutdown() { InvalidatePrepared(); pathGeneration++; }

    private bool CanCancel() => Lifetime == AppLifetime.Running && activeCancellation is { IsCancellationRequested: false } &&
        !cancellationLocked && (MetadataState == MetadataState.Loading || JobState is ClipState.Preparing or ClipState.Executing);

    private void Cancel()
    {
        if (!CanCancel()) return;
        activeCancellation!.Cancel();
        if (JobState is ClipState.Preparing or ClipState.Executing) JobState = ClipState.Cancelling;
        StatusText = "취소 요청 · 작업과 정리가 끝날 때까지 기다립니다.";
        NotifyCommands();
    }

    private async Task PrepareAsync(CancellationToken token, long current)
    {
        long revision = inputRevision, metaRevision = metadataRevision;
        var info = metadata!;
        var request = new ClipRequest(info.Source.PageUrl, Start.Text, End.Text, OutputPath, Quality, Mode);
        JobState = ClipState.Preparing; cancellationLocked = false;
        OperationDetails = "";
        StatusText = "실행 준비 및 도구 진단 중…";
        try
        {
            var result = await Task.Run(() => jobs.PrepareAsync(request, info, token));
            ObserveError(result.Error);
            RecordOperationDetails(result);
            if (Lifetime != AppLifetime.Running || generation != current || revision != inputRevision || metaRevision != metadataRevision || token.IsCancellationRequested)
            { JobState = ClipState.Cancelled; return; }
            if (result.Data is { } value)
            {
                prepared = value; preparedConsumed = false;
                preparedInputRevision = revision; preparedMetadataRevision = metaRevision;
                ConfirmationText = DescribePrepared(value);
                JobState = ClipState.AwaitingConfirmation;
                StatusText = "준비 내용을 확인한 뒤 명시적으로 실행하세요.";
            }
            else
            {
                JobState = result.Status == OperationStatus.Cancelled ? ClipState.Cancelled : ClipState.Failed;
                StatusText = result.Error is { } error ? DescribeError(error) : "준비를 취소했습니다.";
                FocusErrorRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { JobState = ClipState.Cancelled; StatusText = "준비를 취소했습니다."; }
        catch (Exception)
        {
            logger.LogError("Unexpected preparation failure");
            JobState = ClipState.Failed; StatusText = "준비 중 예상하지 못한 오류가 발생했습니다. 다시 준비할 수 있습니다.";
        }
    }

    private async Task ExecuteAsync(CancellationToken token, long current)
    {
        var value = prepared!;
        preparedConsumed = true; prepared = null;
        JobState = ClipState.Executing; acceptingProgress = true; cancellationLocked = false;
        ProgressText = $"{value.JobId} · {value.Start} → {value.End}";
        StatusText = "실행 중…";
        JobStage? lastLoggedStage = null;
        var progress = new Progress<JobProgress>(item =>
        {
            if (!acceptingProgress || generation != current || item.JobId != value.JobId) return;
            if (item.Stage is JobStage.Finalizing or JobStage.Cleaning) cancellationLocked = true;
            ProgressIndeterminate = item.Percent is null || !double.IsFinite(item.Percent.Value) || item.Percent is < 0 or > 100;
            ProgressPercent = ProgressIndeterminate ? 0 : item.Percent!.Value;
            ProgressText = $"{value.JobId} · {value.Start} → {value.End}\n{StageName(item.Stage)} · {item.Message}";
            if (lastLoggedStage != item.Stage)
            {
                logger.LogInformation("Job {JobId} stage {Stage}", item.JobId, item.Stage);
                lastLoggedStage = item.Stage;
            }
            NotifyCommands();
        });
        try
        {
            var result = await Task.Run(() => jobs.ExecuteAsync(value, progress, token));
            acceptingProgress = false;
            LastResult = result; OnPropertyChanged(nameof(LastResult));
            ObserveError(result.Error);
            RetainResources(result.RemainingTemporaryPaths);
            JobState = result.Status switch { OperationStatus.Succeeded => ClipState.Completed, OperationStatus.Cancelled => ClipState.Cancelled, _ => ClipState.Failed };
            StatusText = result.Status switch { OperationStatus.Succeeded => "클립 생성 완료", OperationStatus.Cancelled => "취소 및 정리 종료", _ => "클립 생성 실패" };
            ResultText = DescribeResult(value, result);
            logger.LogInformation("Job {JobId} returned {Status}; code {Code}; category {Category}; field {Field}",
                result.JobId, result.Status, result.Error?.Code, result.Error?.Category, result.Error?.Field);
        }
        catch (Exception)
        {
            acceptingProgress = false; SessionLocked = true; RequiresCloseAcknowledgement = true;
            LastResult = null; OnPropertyChanged(nameof(LastResult));
            JobState = ClipState.Failed;
            StatusText = "실행 예외: 자원 상태를 확인할 수 없어 이 세션의 클립 실행을 잠갔습니다.";
            unresolvedResources.Add($"Job {value.JobId}: 실행 예외로 자원 상태 미확인");
            ResourceWarnings = string.Join("\n", unresolvedResources);
            ResultText = $"JobId {value.JobId}\n요청 {value.Start} → {value.End}\n{StatusText}";
            logger.LogError("Job {JobId} unexpected execution failure", value.JobId);
        }
        finally
        {
            acceptingProgress = false; cancellationLocked = false;
            if (Lifetime == AppLifetime.Running) pathTask = Task.WhenAll(pathTask, CheckPathAsync(OutputPath));
        }
    }

    private void ObserveError(OperationError? error)
    {
        if (error?.Code != OperationErrorCode.ProcessTerminationFailed) return;
        SessionLocked = true; RequiresCloseAcknowledgement = true;
        unresolvedResources.Add("프로세스 종료 미확인. 자원 정리 성공으로 간주하지 않습니다.");
        ResourceWarnings = string.Join("\n", unresolvedResources);
    }

    private void RetainResources(IEnumerable<string> paths)
    {
        foreach (string path in paths) { unresolvedResources.Add("잔존 경로: " + path); RequiresCloseAcknowledgement = true; }
        ResourceWarnings = string.Join("\n", unresolvedResources);
    }

    private void RecordOperationDetails<T>(OperationResult<T> result) where T : class
    {
        OperationDetails = string.Join("\n", (result.Error is { } error ? new[] { DescribeError(error) } : [])
            .Concat(result.Warnings).Concat(result.RemainingTemporaryPaths.Select(p => "잔존 경로: " + p)));
        RetainResources(result.RemainingTemporaryPaths);
    }

    private static string DescribeError(OperationError error) => $"{error.Code} · {error.Field ?? "작업"}: {error.Message}" +
        (error.Code == OperationErrorCode.ProcessTerminationFailed ? "\n프로세스 종료 미확인. 이 세션에서는 클립 실행을 다시 시작할 수 없습니다." : "");

    private static string DescribePrepared(PreparedClip value) =>
        $"JobId {value.JobId}\n{value.Selection.Source.Platform} / {value.Selection.Source.Id}\n{value.Title ?? "제목 미확인"} · {value.Uploader ?? "업로더 미확인"}\n길이 {value.Selection.MetadataDuration}\n" +
        $"{DescribeSelection(value.Selection.Selection)}\n요청 {value.Start} → {value.End} · {value.Selection.Mode}\n" +
        (value.Range is { } range ? $"계산 구간 [{Exact(SignedMediaTime.FromTimecode(range.Start))}, {Exact(SignedMediaTime.FromTimecode(range.End))}) 초\n" : "FPS 미확인: 구간 순서·길이 실행 중 검사 대기\n") +
        $"출력 {value.FinalPath}\n원본 참조 획득 예산 {value.Budget.MaximumBytes} bytes / {Exact(value.Budget.MaximumPresentationSpan)} 초 (출력 길이 상한이 아님)\n" +
        string.Join("\n", value.Warnings.Concat(value.PendingChecks).Distinct());

    private static string DescribeResult(PreparedClip prepared, ClipJobResult result)
    {
        string text = $"이전 작업 결과 · JobId {result.JobId}\n{prepared.Title ?? prepared.Selection.Source.Id}\n요청 {prepared.Start} → {prepared.End} · {prepared.Selection.Mode}\n상태 {result.Status}\n";
        if (result.OutputPath is { } path) text += $"확정 출력 {path}\n";
        if (result.Error is { } error) text += DescribeError(error) + "\n";
        if (result.Presentation is { } p)
        {
            string Range(PresentationRange r) => $"[{Exact(r.Start)}, {Exact(r.End)}) 초";
            text += $"요청 범위 {Range(p.Requested)}\n실제 video {Range(p.Video)}\n실제 audio {(p.Audio is null ? "음성 범위 없음" : Range(p.Audio))}\n" +
                (p.SourceEndVerified ? "원본 전체 끝 확인\n" : "원본 전체 끝 미확인\n") +
                $"삽입 zero {p.InsertedAudioZeroSamples} / 제거 overlap {p.DiscardedAudioOverlapSamples} / priming skip {p.AudioPrimingSkippedSamples} / decoded tail {p.AudioDecodedTailPaddingSamples} samples\n";
        }
        return text + string.Join("\n", result.Warnings.Select(w => "경고: " + w)) + "\n" +
            string.Join("\n", result.RemainingTemporaryPaths.Select(p => "잔존 경로: " + p));
    }

    private static string Exact(SignedMediaTime time) => $"{time.Numerator}/{time.Denominator}";

    private static string StageName(JobStage stage) => stage switch
    {
        JobStage.Preparing => "준비", JobStage.Rechecking => "재검사", JobStage.SourceInspection => "원본 검사",
        JobStage.Downloading => "획득", JobStage.InputInspection => "입력 검사", JobStage.Processing => "미디어 처리",
        JobStage.OutputInspection => "출력 검사", JobStage.Finalizing => "확정", JobStage.Cancelling => "취소",
        JobStage.Cleaning => "정리", JobStage.Completed => "처리 단계 종료 · 최종 결과 대기", _ => "진행 중"
    };
}
