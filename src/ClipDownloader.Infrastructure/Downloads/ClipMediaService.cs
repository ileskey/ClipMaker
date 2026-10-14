using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Infrastructure.Processing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Downloads;

// Site acquisition remains outside this adapter. It composes the already
// verified stage-four local operations and retains all failure resources.
public sealed class ClipMediaService : IClipMediaService
{
    private sealed class Job
    {
        internal readonly List<Guid> ProcessOperations = [], RenderOperations = [];
        internal bool Unconfirmed;
        internal bool UnconfirmedHandles;
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Job> jobs = [];
    private readonly AcquiredMediaProbeService inspection;
    private readonly FfmpegMediaProcessingService processor;
    private readonly AudioPcmRenderService render = new();
    private readonly SourceAudioPreparationService preparation;
    private readonly FastClipVerificationService fast;
    private readonly AccurateClipVerificationService accurate;
    public ClipMediaService(IMediaProbe probe, IToolLocator locator, ToolPathSettings settings, IProcessRunner runner)
    {
        inspection = new(probe); processor = new(locator, settings, runner); preparation = new(processor);
        fast = new(probe, locator, settings, runner); accurate = new(probe, locator, settings, runner);
    }
    public ClipMediaResources GetOwnedResources(Guid jobId)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(jobId, out var job)) return new([], false);
            var processing = job.ProcessOperations.Select(processor.GetOwnedResources).ToArray();
            var rendering = job.RenderOperations.Select(render.GetOwnedResources).ToArray();
            return new(processing.Concat(rendering).SelectMany(r => r.Files).GroupBy(f => f.Id).Select(g => g.Last()),
                job.Unconfirmed || processing.Any(r => r.HasUnconfirmedProcess) || job.ProcessOperations.Any(id => fast.HasUnconfirmedProcess(id) || accurate.HasUnconfirmedProcess(id)),
                job.UnconfirmedHandles || processing.Concat(rendering).Any(r => r.HasUnconfirmedFileHandles));
        }
    }
    public async Task<OperationResult<VerifiedClipOutput>> ProcessAsync(AcquiredMedia acquired, Guid stagingFileId, string stagingPath,
        IProgress<JobProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(acquired); Job job;
        lock (gate)
        {
            if (jobs.ContainsKey(acquired.Request.JobId)) throw new InvalidOperationException("Media composition may only run once per job.");
            if (jobs.Values.Any(j => j.Unconfirmed)) return OperationResult<VerifiedClipOutput>.Failed(new(
                OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "이전 미디어 작업의 종료가 확인되지 않았습니다.", "PreviousMediaTermination"));
            job = new(); jobs.Add(acquired.Request.JobId, job);
        }
        var result = await Run().ConfigureAwait(false);
        if (result.Error?.Code == OperationErrorCode.ProcessTerminationFailed) lock (gate) job.Unconfirmed = true;
        if (result.Error?.Field is "VerificationReadClose" or "AccurateVerificationClose") lock (gate) job.UnconfirmedHandles = true;
        return result;

        void Report(JobStage stage, string message, double? percent = null) => progress?.Report(new(acquired.Request.JobId, stage, percent, message));
        async Task<OperationResult<VerifiedClipOutput>> Run()
        {
            if (token.IsCancellationRequested) return OperationResult<VerifiedClipOutput>.Cancelled();
            Report(JobStage.InputInspection, "획득한 입력의 바이트·native 시간·필수 스트림을 검사합니다.");
            var inspected = await inspection.InspectAsync(acquired, token).ConfigureAwait(false);
            if (inspected.Status != OperationStatus.Succeeded) return Translate(inspected);
            MediaInspectionFact<MediaRandomAccessPoint>? key = null;
            MediaInspectionFact<AudioDecoderPreparation>? prepared = null;
            if (acquired.Request.Selection.Mode == ClipMode.Fast)
            {
                var random = await new ClipRandomAccessInspector().InspectAsync(inspected.Data!, token).ConfigureAwait(false);
                if (random.Status != OperationStatus.Succeeded) return Translate(random);
                key = random.Data;
            }
            else if (inspected.Data!.Streams.Any(s => s.Native.Stream.Kind == MediaStreamKind.Audio))
            {
                Report(JobStage.Processing, "원본 시작부터 음성 값을 준비합니다.");
                Guid operation = Guid.NewGuid(); lock (gate) job.ProcessOperations.Add(operation);
                var decoded = await preparation.PrepareAsync(inspected.Data, operation, Guid.NewGuid(),
                    Path.Combine(acquired.Request.WorkDirectory, "decoded-" + operation.ToString("N") + ".f32le"), token).ConfigureAwait(false);
                if (decoded.Status != OperationStatus.Succeeded) return Translate(decoded);
                prepared = decoded.Data;
            }
            var qualified = new ClipProcessingQualifier().Qualify(inspected.Data!, key, prepared);
            if (qualified.Status != OperationStatus.Succeeded) return Translate(qualified);
            MediaAudioRenderInput? rendered = null;
            if (qualified.Data!.Processing.AudioBoundary is not null)
            {
                Guid operation = Guid.NewGuid(); lock (gate) job.RenderOperations.Add(operation);
                var values = await render.RenderAsync(qualified.Data, operation, Guid.NewGuid(),
                    Path.Combine(acquired.Request.WorkDirectory, "render-" + operation.ToString("N") + ".f32le"), token).ConfigureAwait(false);
                if (values.Status != OperationStatus.Succeeded) return Translate(values);
                rendered = values.Data;
            }
            Report(JobStage.Processing, "검증한 계획으로 출력 staging을 생성합니다.");
            Guid processId = Guid.NewGuid(); lock (gate) job.ProcessOperations.Add(processId);
            var request = new MediaProcessingRequest(processId, stagingFileId, qualified.Data.Processing, stagingPath, rendered);
            var processed = await processor.ProcessAsync(request, new InlineProgress<MediaProcessingProgress>(p => Report(JobStage.Processing, p.Message, p.Percent)), token).ConfigureAwait(false);
            if (processed.Status == MediaOperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
            if (processed.Status != MediaOperationStatus.Succeeded) return OperationResult<VerifiedClipOutput>.Failed(Map(processed.Failure!));
            if (token.IsCancellationRequested) return OperationResult<VerifiedClipOutput>.Cancelled();
            Report(JobStage.OutputInspection, "실제 출력의 frame·sample·표시 끝을 검사합니다.");
            return acquired.Request.Selection.Mode == ClipMode.Fast
                ? await fast.VerifyAsync(qualified.Data, processed.Data!, token).ConfigureAwait(false)
                : await accurate.VerifyAsync(qualified.Data, processed.Data!, token).ConfigureAwait(false);
        }
    }
    private static OperationResult<VerifiedClipOutput> Translate<T>(OperationResult<T> result) where T : class => result.Status == OperationStatus.Cancelled ?
        OperationResult<VerifiedClipOutput>.Cancelled(result.Warnings, result.RemainingTemporaryPaths) : OperationResult<VerifiedClipOutput>.Failed(result.Error!, result.Warnings, result.RemainingTemporaryPaths);
    private static OperationError Map(MediaFailure failure) => new(failure.Kind switch {
        MediaFailureKind.TerminationUnconfirmed => OperationErrorCode.ProcessTerminationFailed, MediaFailureKind.MissingTool => OperationErrorCode.MissingTool,
        MediaFailureKind.TimedOut => OperationErrorCode.TimedOut, MediaFailureKind.UnsupportedInput => OperationErrorCode.UnsupportedFormat,
        MediaFailureKind.ProcessFailed => OperationErrorCode.ProcessFailed, MediaFailureKind.OutputFailed when failure.Field == "OutputConflict" => OperationErrorCode.OutputConflict,
        MediaFailureKind.OutputFailed => OperationErrorCode.OutputFailed, _ => OperationErrorCode.VerificationFailed }, OperationErrorCategory.Execution, failure.Message, failure.Field);
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
