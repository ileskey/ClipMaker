using System.ComponentModel;
using System.IO;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.Dialogs;
using ClipMaker.Media.Core.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ClipDownloader.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IVideoInfoService videoInfo;
    private readonly IVideoSourceParser sourceParser;
    private readonly IOutputPathValidator outputValidator;
    private readonly IOutputFileDialog outputDialog;
    private readonly IClipJobService jobs;
    private readonly FormatSelector selector = new();
    private VideoInfo? metadata;
    private FormatSelection? selection;
    private long inputRevision;
    private long metadataRevision;
    private long pathGeneration;
    private Task pathTask = Task.CompletedTask;

    [ObservableProperty] private string url = "";
    [ObservableProperty] private string outputPath = "";
    [ObservableProperty] private QualityOption quality = QualityOption.Best;
    [ObservableProperty] private ClipMode mode;
    [ObservableProperty] private IReadOnlyList<QualityOption> qualities = [QualityOption.Best];
    [ObservableProperty] private string metadataText = "영상 정보 없음";
    [ObservableProperty] private string selectionText = "선택 정보 없음";
    [ObservableProperty] private string validationSummary = "";
    [ObservableProperty] private bool pathChecking;
    [ObservableProperty] private bool dialogOpen;
    public TimecodeInputViewModel Start { get; } = new();
    public TimecodeInputViewModel End { get; } = new();
    public IReadOnlyList<ClipMode> Modes { get; } = [ClipMode.Fast, ClipMode.Accurate];
    public IAsyncRelayCommand LoadMetadataCommand { get; private set; } = null!;
    public IRelayCommand BrowseOutputCommand { get; private set; } = null!;
    public IRelayCommand CancelCommand { get; private set; } = null!;
    public event EventHandler? FocusErrorRequested;
    private bool CanLoad => CanEdit && ToolsInitialized;
    private bool InputsValid => MetadataState == MetadataState.Ready && selection is not null &&
        !HasErrors && !Start.HasErrors && !End.HasErrors && !PathChecking;

    private void InitializeClipCommands()
    {
        LoadMetadataCommand = new AsyncRelayCommand(() => CanLoad ? StartOperationAsync(LoadMetadataAsync) : Task.CompletedTask, () => CanLoad);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => CanEdit);
        CancelCommand = new RelayCommand(Cancel, CanCancel);
        Start.PropertyChanged += TimeChanged;
        End.PropertyChanged += TimeChanged;
        InitializeJobCommands();
        ValidateInputs();
    }

    private void TimeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimecodeInputViewModel.Hours) or nameof(TimecodeInputViewModel.Minutes) or
            nameof(TimecodeInputViewModel.Seconds) or nameof(TimecodeInputViewModel.Frames)) InputChanged();
    }

    partial void OnUrlChanged(string value)
    {
        metadata = null; selection = null; metadataRevision++;
        MetadataState = MetadataState.Empty;
        MetadataText = "영상 정보 없음";
        ClearMetadataPresentation();
        Qualities = [QualityOption.Best];
        InputChanged();
    }
    partial void OnOutputPathChanged(string value) { InputChanged(); pathTask = Task.WhenAll(pathTask, CheckPathAsync(value)); }
    partial void OnQualityChanged(QualityOption value) { InputChanged(); StatusText = "선택 FPS로 시간을 다시 검사했습니다. 입력 숫자는 유지합니다."; }
    partial void OnModeChanged(ClipMode value) => InputChanged();
    partial void OnDialogOpenChanged(bool value) => NotifyCommands();

    private void InputChanged()
    {
        inputRevision++;
        InvalidatePrepared();
        ValidateInputs();
    }

    private void ValidateInputs()
    {
        SetErrors(nameof(Url), ParseUrl().Error is { } urlError ? [urlError.Message] : []);
        selection = null;
        if (metadata is not null)
        {
            var selected = selector.Select(metadata, Quality);
            selection = selected.Data;
            SetErrors(nameof(Quality), selected.Error is { } error ? [error.Message] : []);
        }
        else SetErrors(nameof(Quality), []);
        SetErrors(nameof(Mode), Enum.IsDefined(Mode) ? [] : ["Fast 또는 Accurate 모드를 선택하세요."]);
        var fps = selection?.VideoFormat.FrameRate;
        bool startValid = Start.TryGet(fps, out _, out var start);
        bool endValid = End.TryGet(fps, out _, out var end);
        string[] rangeErrors = [];
        if (fps is not null && startValid && endValid)
        {
            if (!MediaTimeRange.TryCreate(start!, end!, out var range, out var error) ||
                metadata?.Duration is { } duration && !range!.TryValidateDuration(duration, out error))
                rangeErrors = [error!.Message];
        }
        SetErrors("Range", rangeErrors);
        SelectionText = selection is null ? "선택 정보 없음" : DescribeSelection(selection) +
            (fps is null ? "\nFPS 미확인: FF 상한·구간 순서·길이는 실행 중 검사 대기" : "\n시계 시간 + FF/FPS · 구간 [Start, End)");
        RefreshMetadataPresentation();
        UpdateValidationSummary();
        NotifyCommands();
    }

    private OperationResult<VideoSourceReference> ParseUrl() => Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri)
        ? sourceParser.Parse(uri) : OperationResult<VideoSourceReference>.Failed(new(OperationErrorCode.InvalidInput, OperationErrorCategory.Input, "올바른 영상 URL을 입력하세요.", "Url"));

    private async Task LoadMetadataAsync(CancellationToken token, long current)
    {
        string requestedUrl = Url;
        var parsed = ParseUrl();
        InputChanged();
        metadata = null; selection = null; metadataRevision++;
        MetadataText = "영상 정보 없음";
        ClearMetadataPresentation();
        MetadataState = MetadataState.Empty;
        Quality = QualityOption.Best;
        Qualities = [QualityOption.Best];
        ValidateInputs();
        if (!string.Equals(Url, requestedUrl, StringComparison.Ordinal) || Lifetime != AppLifetime.Running || token.IsCancellationRequested) return;
        if (parsed.Data is null) { StatusText = parsed.Error!.Message; FocusErrorRequested?.Invoke(this, EventArgs.Empty); return; }
        long revision = inputRevision;
        bool IsCurrent() => Lifetime == AppLifetime.Running && generation == current && revision == inputRevision;
        MetadataState = MetadataState.Loading;
        OperationDetails = "";
        JobState = ClipState.Idle;
        StatusText = "영상 정보 조회 중…";
        try
        {
            var result = await Task.Run(() => videoInfo.GetVideoInfoAsync(parsed.Data.PageUrl, token));
            ObserveError(result.Error);
            RetainResources(result.RemainingTemporaryPaths);
            if (!IsCurrent()) return;
            if (token.IsCancellationRequested) { MetadataState = MetadataState.Empty; StatusText = "조회를 취소했습니다."; return; }
            RecordOperationDetails(result);
            if (result.Data is { } info && info.State == VideoState.Completed && info.Duration > TimeSpan.Zero)
            {
                metadata = info; metadataRevision++;
                long acceptedRevision = metadataRevision;
                bool IsAccepted() => Lifetime == AppLifetime.Running && generation == current && metadataRevision == acceptedRevision &&
                    ReferenceEquals(metadata, info) && !token.IsCancellationRequested;
                Start.Reset();
                if (!IsAccepted()) return;
                End.Reset();
                if (!IsAccepted()) return;
                Quality = QualityOption.Best;
                if (!IsAccepted()) return;
                Qualities = MetadataPresentation.QualityOptions(info);
                if (!IsAccepted()) return;
                MetadataText = $"{info.Source.Platform} / {info.Source.Id}\n{info.Title ?? "제목 미확인"} · {info.Uploader ?? "업로더 미확인"}\n길이: {info.Duration}";
                if (!IsAccepted()) return;
                ValidateInputs();
                if (!IsAccepted()) return;
                ThumbnailStatus = info.ThumbnailUrl is null ? "썸네일 없음" : "썸네일 획득 연결 대기";
                if (!IsAccepted()) return;
                MetadataState = MetadataState.Ready;
                if (!IsAccepted()) return;
                StatusText = "영상 조회 완료. 종료 시간과 MP4 출력 경로를 지정하세요.";
                StartThumbnail(info);
            }
            else
            {
                MetadataState = result.Status == OperationStatus.Cancelled ? MetadataState.Empty : MetadataState.Failed;
                StatusText = result.Error?.Message ?? "완료 상태와 양의 길이를 확인한 영상이 필요합니다.";
                FocusErrorRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (IsCurrent()) { MetadataState = MetadataState.Empty; StatusText = "조회를 취소했습니다."; } }
        catch (Exception)
        {
            logger.LogError("Unexpected metadata failure");
            if (IsCurrent()) { MetadataState = MetadataState.Failed; StatusText = "영상 정보 조회 중 예상하지 못한 오류가 발생했습니다."; }
        }
        finally { if (IsCurrent()) ValidateInputs(); else if (Lifetime != AppLifetime.Running) MetadataState = MetadataState.Empty; }
    }

    private async Task CheckPathAsync(string path)
    {
        long current = ++pathGeneration;
        PathChecking = true;
        SetErrors(nameof(OutputPath), ["출력 경로 검사 중…"]);
        UpdateValidationSummary(); NotifyCommands();
        try
        {
            await Task.Delay(300);
            if (current != pathGeneration || Lifetime != AppLifetime.Running) return;
            var result = await Task.Run(() => Path.IsPathFullyQualified(path)
                ? outputValidator.Validate(path) : OperationResult<string>.Failed(new(OperationErrorCode.InvalidOutputPath, OperationErrorCategory.Input, "기존 폴더의 MP4 절대 경로를 지정하세요.", "Output")));
            if (current != pathGeneration || Lifetime != AppLifetime.Running) return;
            SetErrors(nameof(OutputPath), result.Error is { } error ? [error.Message] : []);
        }
        catch (Exception) { if (current == pathGeneration) SetErrors(nameof(OutputPath), ["출력 경로를 검사할 수 없습니다."]); }
        finally
        {
            if (current == pathGeneration) { PathChecking = false; UpdateValidationSummary(); NotifyCommands(); }
        }
    }

    private void BrowseOutput()
    {
        if (!CanEdit) return;
        DialogOpen = true;
        try
        {
            string? path = outputDialog.ShowSaveFileDialog(OutputFileNameSuggestion.Create(metadata?.Title, metadata?.Source.Id, Start.Text, End.Text), OutputPath);
            if (path is not null) OutputPath = path;
        }
        catch (Exception) { StatusText = "파일 선택 창을 열 수 없습니다."; FocusErrorRequested?.Invoke(this, EventArgs.Empty); }
        finally { DialogOpen = false; }
    }

    private void UpdateValidationSummary() => ValidationSummary = string.Join("\n", GetErrors(null).Cast<string>()
        .Concat(Start.GetErrors(null).Cast<string>().Select(e => "Start: " + e)).Concat(End.GetErrors(null).Cast<string>().Select(e => "End: " + e)));

    private static string DescribeSelection(FormatSelection value)
    {
        var video = value.VideoFormat; var audio = value.AudioFormat ?? video;
        string quality = value.RequestedQuality.Kind == QualityKind.Best ? "Best" : $"{value.RequestedQuality.Height}p";
        string presence = audio.AudioStream switch { StreamPresence.Present => "있음", StreamPresence.Absent => "없음", _ => "미확인" };
        return $"{quality} → video {video.Id}, audio {audio.Id} · {video.Width}×{video.Height} · FPS {video.FrameRate?.ToString() ?? "미확인"}\n코덱 {video.VideoCodec ?? "미확인"} / {audio.AudioCodec ?? "미확인"} · 음성 선언 {presence}\n{string.Join("\n", value.Warnings.Select(SelectionWarning))}";
    }

    private static string SelectionWarning(FormatSelectionWarning warning) => warning switch
    {
        FormatSelectionWarning.DeclaredQualityOnly => "선택 화질은 metadata 선언이며 실행 지원 보장이 아닙니다.",
        FormatSelectionWarning.VideoStreamUnknown => "영상 스트림 미확인",
        FormatSelectionWarning.AudioStreamUnknown => "음성 스트림 미확인",
        FormatSelectionWarning.AudioSourceRequiresVerification => "원본 음성의 실행 중 검사가 필요합니다.",
        FormatSelectionWarning.ResolutionUnknown => "해상도 미확인",
        FormatSelectionWarning.FrameRateUnknown => "선택 포맷 FPS 미확인",
        FormatSelectionWarning.VideoCodecUnknown => "영상 코덱 미확인",
        FormatSelectionWarning.AudioCodecUnknown => "음성 코덱 미확인",
        FormatSelectionWarning.RankingMetadataIncomplete => "일부 포맷의 비교 정보가 미확인입니다.",
        FormatSelectionWarning.StableIdTieBreak => "같은 우선순위에서 포맷 ID 순으로 선택했습니다.",
        _ => "선택 정보의 추가 검사가 필요합니다."
    };
}
