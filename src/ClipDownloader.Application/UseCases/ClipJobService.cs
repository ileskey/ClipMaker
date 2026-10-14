using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.UseCases;

public sealed class ClipJobService(IVideoInfoService metadata, IVideoSourceParser sources, IToolExecutionDiagnostics diagnostics, IOutputPathValidator outputPaths,
    IOutputWorkspace workspace, ISelectedSourceInspectionService sourceInspection, IClipDownloadService acquisition,
    ISourceResourceAccess sourceResources, IClipMediaService media, SourceInspectionBudget budget) : IClipJobService
{
    private readonly object gate = new();
    private readonly HashSet<Guid> started = [];
    private bool previousTerminationUnconfirmed;
    public async Task<OperationResult<PreparedClip>> PrepareAsync(ClipRequest request, VideoInfo videoInfo, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(videoInfo);
        if (token.IsCancellationRequested) return OperationResult<PreparedClip>.Cancelled();
        var source = sources.Parse(request.Url);
        if (source.Status != OperationStatus.Succeeded) return Translate<PreparedClip, VideoSourceReference>(source);
        if (source.Data != videoInfo.Source || !Enum.IsDefined(request.Mode)) return Failed<PreparedClip>(OperationErrorCode.InvalidInput, "Source", OperationErrorCategory.Input);
        if (videoInfo.State != VideoState.Completed) return Failed<PreparedClip>(OperationErrorCode.SourceUnavailable, "VideoState");
        if (videoInfo.Duration is null or { Ticks: <= 0 }) return Failed<PreparedClip>(OperationErrorCode.InvalidMetadata, "Duration");
        if (!TimecodeInput.TryParse(request.Start, out var start, out var error)) return TimeFailure<PreparedClip>(error!, syntax: true);
        if (!TimecodeInput.TryParse(request.End, out var end, out error)) return TimeFailure<PreparedClip>(error!, syntax: true);
        if (start.Minutes > 59 || start.Seconds > 59 || end.Minutes > 59 || end.Seconds > 59)
            return Failed<PreparedClip>(OperationErrorCode.InvalidRange, "Timecode", OperationErrorCategory.Input);
        var selection = new FormatSelector().Select(videoInfo, request.Quality ?? QualityOption.Best);
        if (selection.Status != OperationStatus.Succeeded) return OperationResult<PreparedClip>.Failed(selection.Error!);
        var snapshot = SelectedSourceSnapshot.Create(Guid.NewGuid(), videoInfo, selection.Data!, request.Mode);
        if (snapshot.Status != OperationStatus.Succeeded) return OperationResult<PreparedClip>.Failed(snapshot.Error!);
        MediaTimeRange? range = null;
        if (selection.Data!.VideoFormat.FrameRate is { } rate)
        {
            var checkedRange = ValidateRange(start, end, rate, videoInfo.Duration.Value);
            if (checkedRange.Status != OperationStatus.Succeeded) return OperationResult<PreparedClip>.Failed(checkedRange.Error!);
            range = checkedRange.Data;
        }
        var output = outputPaths.Validate(request.OutputPath);
        if (output.Status != OperationStatus.Succeeded) return OperationResult<PreparedClip>.Failed(output.Error!);
        var diagnostic = await CheckTools(request.Mode, token).ConfigureAwait(false);
        if (diagnostic.Status == OperationStatus.Cancelled) return OperationResult<PreparedClip>.Cancelled();
        if (diagnostic.Status != OperationStatus.Succeeded) return OperationResult<PreparedClip>.Failed(diagnostic.Error!);
        if (token.IsCancellationRequested) return OperationResult<PreparedClip>.Cancelled();
        var pending = new List<string> { "선택 원본의 시간축·획득 범위·코덱·실제 출력은 실행 중 확인합니다." };
        if (range is null) pending.Add("FPS 미확인으로 프레임 범위 계산은 실행 중 검사 이후 수행합니다.");
        if (request.Mode == ClipMode.Accurate) pending.Add("원본 시작부터 필요한 참조를 획득 상한 안에서 확보할 수 있는지 실행 중 확인합니다.");
        // Public messages are host-independent; no console or --yes is read here.
        return OperationResult<PreparedClip>.Succeeded(new(Guid.NewGuid(), snapshot.Data!, start, end, range, output.Data!,
            ["선택 화질은 메타데이터의 선언을 기준으로 합니다.", request.Mode == ClipMode.Fast ? "Fast의 실제 시작·끝은 요청 범위와 다를 수 있습니다." :
                "Accurate는 검증한 원본 시작 참조와 sample-grid 표현 범위에서 실행합니다."], pending, budget, videoInfo.Title, videoInfo.Uploader));
    }

    public async Task<ClipJobResult> ExecuteAsync(PreparedClip prepared, IProgress<JobProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        lock (gate)
        {
            if (!started.Add(prepared.JobId)) throw new InvalidOperationException("A prepared job may only execute once.");
            if (previousTerminationUnconfirmed) return new(prepared.JobId, OperationStatus.Failed, null,
                new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "이전 작업의 프로세스 종료가 확인되지 않았습니다.", "PreviousJobTermination"), prepared.Warnings, []);
        }
        using var finalization = new ClipFinalizationGate(token);
        IOutputSession? session = null; VerifiedClipOutput? verified = null;
        OperationStatus status = OperationStatus.Failed; OperationError? error = null; string? finalPath = null;
        var warnings = new List<string>(prepared.Warnings); var remaining = new List<string>();
        try
        {
            var outcome = await Run().ConfigureAwait(false); status = outcome.Status; error = outcome.Error; finalPath = outcome.Data;
            warnings.AddRange(outcome.Warnings); remaining.AddRange(outcome.RemainingTemporaryPaths);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = OperationStatus.Cancelled; }
        finally
        {
            if (session is not null)
            {
                var source = sourceResources.GetOwnedResources(prepared.JobId); var processed = media.GetOwnedResources(prepared.JobId);
                session.TrackOwnedFiles(source.Files.Select(f => new MediaOwnedProcessingFile(f.Id, f.Path, f.Content)).Concat(processed.Files));
                bool unconfirmed = source.HasUnconfirmedProcess || processed.HasUnconfirmedProcess || error?.Code == OperationErrorCode.ProcessTerminationFailed;
                bool preserveHandles = source.Files.Any(f => f.MayBeInUse) || processed.HasUnconfirmedFileHandles;
                if (unconfirmed)
                { status = OperationStatus.Failed; finalPath = null; error = new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "작업 프로세스의 종료가 확인되지 않았습니다."); }
                try
                {
                    if (status == OperationStatus.Cancelled) Report(JobStage.Cancelling, "작업 취소 후 자원을 정리합니다.");
                    Report(JobStage.Cleaning, "작업이 생성한 임시 파일을 정리합니다.");
                }
                finally
                {
                    var cleanup = await session.CleanupAsync(unconfirmed || preserveHandles ? WorkspaceCleanupMode.PreservePotentiallyInUse : WorkspaceCleanupMode.DeleteOwnedTemporaryFiles).ConfigureAwait(false);
                    warnings.AddRange(cleanup.Warnings); remaining.AddRange(cleanup.RemainingPaths);
                }
            }
        }
        if (error?.Code == OperationErrorCode.ProcessTerminationFailed) lock (gate) previousTerminationUnconfirmed = true;
        Report(JobStage.Completed, status == OperationStatus.Succeeded ? "최종 파일 확정을 완료했습니다." : status == OperationStatus.Cancelled ? "작업을 취소했습니다." : "작업에 실패했습니다.");
        return new(prepared.JobId, status, finalPath, error, warnings.Distinct(), remaining.Distinct(StringComparer.OrdinalIgnoreCase),
            status == OperationStatus.Succeeded ? ClipPresentationSummary.FromVerified(verified!) : null);

        void Report(JobStage stage, string message, double? percent = null) => progress?.Report(new(prepared.JobId, stage, percent, message));
        async Task<OperationResult<string>> Run()
        {
            token.ThrowIfCancellationRequested(); Report(JobStage.Rechecking, "선택과 실행 조건을 다시 확인합니다.");
            var path = outputPaths.Validate(prepared.FinalPath, OperationErrorCategory.Execution);
            if (path.Status != OperationStatus.Succeeded) return OperationResult<string>.Failed(path.Error!);
            var tools = await CheckTools(prepared.Selection.Mode, token).ConfigureAwait(false);
            if (tools.Status != OperationStatus.Succeeded) return tools;
            var refreshed = await metadata.GetVideoInfoAsync(prepared.Selection.Source.PageUrl, token).ConfigureAwait(false);
            if (refreshed.Status != OperationStatus.Succeeded) return Translate<string, VideoInfo>(refreshed);
            var selection = RefreshSelection(prepared.Selection, refreshed.Data!);
            if (selection.Status != OperationStatus.Succeeded) return Translate<string, SelectedSourceSnapshot>(selection);
            token.ThrowIfCancellationRequested();
            var created = await workspace.CreateAsync(prepared.JobId, prepared.FinalPath, token).ConfigureAwait(false);
            if (created.Status != OperationStatus.Succeeded) return Translate<string, IOutputSession>(created);
            session = created.Data!;
            Report(JobStage.SourceInspection, "선택 source의 FPS와 시간축을 검사합니다.");
            var inspectionRequest = new SelectedSourceInspectionRequest(prepared.JobId, selection.Data!,
                SourceInspectionPurpose.FrameRate | SourceInspectionPurpose.SourceTimeline, session.WorkDirectory, prepared.Budget);
            var inspected = await sourceInspection.InspectAsync(inspectionRequest, token).ConfigureAwait(false);
            if (inspected.Status != OperationStatus.Succeeded) return Translate<string, SelectedSourceInspection>(inspected);
            if (!ReferenceEquals(inspected.Data!.Request, inspectionRequest)) return Failed<string>(OperationErrorCode.VerificationFailed, "SourceInspectionIdentity");
            if (inspected.Data!.FrameRate.State != MediaInspectionState.Verified) return Failed<string>(OperationErrorCode.UnknownFrameRate, "FrameRate");
            var rate = inspected.Data.FrameRate.Value!;
            if (prepared.Range is not null && prepared.Range.FrameRate != rate) return Failed<string>(OperationErrorCode.InvalidMetadata, "SelectedFrameRateChanged");
            if (selection.Data!.Selection.VideoFormat.FrameRate is { } declared && declared != rate) return Failed<string>(OperationErrorCode.InvalidMetadata, "SelectedFrameRateChanged");
            var range = ValidateRange(prepared.Start, prepared.End, rate, selection.Data!.MetadataDuration!.Value);
            if (range.Status != OperationStatus.Succeeded) return Translate<string, MediaTimeRange>(range);
            token.ThrowIfCancellationRequested(); Report(JobStage.Downloading, "확인한 범위의 원본 참조를 획득합니다.");
            var acquisitionRequest = new AcquisitionRequest(prepared.JobId, selection.Data, inspected.Data, range.Data!, session.WorkDirectory);
            var acquired = await acquisition.AcquireAsync(acquisitionRequest,
                new InlineProgress<AcquisitionProgress>(p => Report(JobStage.Downloading, p.Message, p.Percent)), token).ConfigureAwait(false);
            if (acquired.Status != OperationStatus.Succeeded) return Translate<string, AcquiredMedia>(acquired);
            if (!ReferenceEquals(acquired.Data!.Request, acquisitionRequest)) return Failed<string>(OperationErrorCode.VerificationFailed, "AcquisitionIdentity");
            token.ThrowIfCancellationRequested();
            var output = await media.ProcessAsync(acquired.Data!, session.StagingFileId, session.StagingPath, progress, token).ConfigureAwait(false);
            if (output.Status != OperationStatus.Succeeded) return Translate<string, VerifiedClipOutput>(output);
            verified = output.Data!;
            if (!ReferenceEquals(verified.Plan.Inspection.Acquisition, acquired.Data)) return Failed<string>(OperationErrorCode.VerificationFailed, "VerifiedAcquisitionIdentity");
            session.TrackOwnedFiles(media.GetOwnedResources(prepared.JobId).Files);
            if (!finalization.TryBeginFinalizing()) return OperationResult<string>.Cancelled();
            Report(JobStage.Finalizing, "검증한 staging을 최종 이름으로 확정합니다.");
            return await session.CommitAsync(verified.Processing.Staging).ConfigureAwait(false);
        }
    }

    private async Task<OperationResult<string>> CheckTools(ClipMode mode, CancellationToken token)
    {
        var features = new List<ToolFeatureRequirement> { new(ToolFeatureKind.Muxer, "mp4") };
        if (mode == ClipMode.Accurate) features.AddRange([new(ToolFeatureKind.Encoder, "libx264"), new(ToolFeatureKind.Encoder, "aac")]);
        var checkedTools = await diagnostics.DiagnoseAsync(new(ToolDiagnosticCommand.Clip, features), token).ConfigureAwait(false);
        if (checkedTools.Status != OperationStatus.Succeeded) return Translate<string, ToolExecutionReport>(checkedTools);
        var report = checkedTools.Data!;
        if (report.Command != ToolDiagnosticCommand.Clip) return Failed<string>(OperationErrorCode.VerificationFailed, "DiagnosticCommand");
        if (!report.AllRequestedChecksPassed) return OperationResult<string>.Failed(report.Tools.Select(t => t.Error).FirstOrDefault(e => e is not null) ??
            new(OperationErrorCode.ProcessFailed, OperationErrorCategory.Execution, "필수 도구 실행 검사를 통과하지 못했습니다.", "Tools"));
        var ffmpeg = report.Tools.Single(t => t.Tool == ToolKind.Ffmpeg);
        if (features.Any(f => !ffmpeg.Features.Any(c => c.Requirement == f && c.Status == ToolCheckStatus.Passed)))
            return Failed<string>(OperationErrorCode.UnsupportedFormat, "RequiredFeatures");
        return OperationResult<string>.Succeeded("Checked");
    }
    private static OperationResult<SelectedSourceSnapshot> RefreshSelection(SelectedSourceSnapshot prior, VideoInfo info)
    {
        if (info.Formats.Any(f => f is null) || info.Formats.GroupBy(f => f.Id, StringComparer.Ordinal).Any(g => g.Count() != 1))
            return Failed<SelectedSourceSnapshot>(OperationErrorCode.InvalidMetadata, "FormatIdentity");
        if (info.Source != prior.Source || info.State != VideoState.Completed) return Failed<SelectedSourceSnapshot>(OperationErrorCode.SourceUnavailable, "SourceChanged");
        if (info.Duration != prior.MetadataDuration) return Failed<SelectedSourceSnapshot>(OperationErrorCode.InvalidMetadata, "DurationChanged");
        var video = info.Formats.SingleOrDefault(f => f.Id == prior.Selection.VideoFormat.Id);
        var audio = prior.Selection.AudioFormat is null ? null : info.Formats.SingleOrDefault(f => f.Id == prior.Selection.AudioFormat.Id);
        if (video is null || prior.Selection.AudioFormat is not null && audio is null) return Failed<SelectedSourceSnapshot>(OperationErrorCode.UnsupportedFormat, "SelectedFormatMissing");
        var oldVideo = prior.Selection.VideoFormat;
        if (oldVideo.FrameRate is null) oldVideo = oldVideo with { FrameRate = video.FrameRate };
        if (video != oldVideo || audio != prior.Selection.AudioFormat) return Failed<SelectedSourceSnapshot>(OperationErrorCode.InvalidMetadata, "SelectedFormatChanged");
        var selected = FormatSelection.Create(info, prior.Selection.RequestedQuality, video, audio, prior.Selection.Reason,
            prior.Selection.Warnings, prior.Selection.RankingCriteria);
        return selected.Status == OperationStatus.Succeeded ? SelectedSourceSnapshot.Create(prior.SnapshotId, info, selected.Data!, prior.Mode) :
            OperationResult<SelectedSourceSnapshot>.Failed(selected.Error!);
    }
    private static OperationResult<MediaTimeRange> ValidateRange(TimecodeInput start, TimecodeInput end, FrameRate rate, TimeSpan duration)
    {
        if (!Timecode.TryCreate(start, rate, out var first, out var error) || !Timecode.TryCreate(end, rate, out var last, out error) ||
            !MediaTimeRange.TryCreate(first!, last!, out var range, out error) || !range.TryValidateDuration(duration, out error))
            return TimeFailure<MediaTimeRange>(error!, syntax: false);
        return OperationResult<MediaTimeRange>.Succeeded(range);
    }
    private static OperationResult<T> TimeFailure<T>(TimeValidationError error, bool syntax) where T : class =>
        OperationResult<T>.Failed(new(syntax ? OperationErrorCode.InvalidInput : OperationErrorCode.InvalidRange, OperationErrorCategory.Input, error.Message, error.Field));
    private static OperationResult<T> Failed<T>(OperationErrorCode code, string field, OperationErrorCategory category = OperationErrorCategory.Execution) where T : class =>
        OperationResult<T>.Failed(new(code, category, "작업의 입력 또는 필수 실행 조건을 충족하지 못했습니다.", field));
    private static OperationResult<T> Translate<T, U>(OperationResult<U> result) where T : class where U : class =>
        result.Status == OperationStatus.Cancelled ? OperationResult<T>.Cancelled(result.Warnings, result.RemainingTemporaryPaths) : OperationResult<T>.Failed(result.Error!, result.Warnings, result.RemainingTemporaryPaths);
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
