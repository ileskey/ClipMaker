using System.Numerics;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed class AccurateClipVerificationService
{
    private readonly IMediaProbe probe;
    private readonly Func<CollectedSourceReference, CancellationToken, Task<OperationResult<DecodedPictureObservations>>> pictures;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object gate = new();
    private readonly HashSet<Guid> unconfirmed = [];
    public AccurateClipVerificationService(IMediaProbe probe, IToolLocator locator, ToolPathSettings settings, IProcessRunner runner)
    { this.probe = probe; pictures = new PresentationVideoProbe(locator, settings, runner).DecodeAsync; }
    internal AccurateClipVerificationService(IMediaProbe probe,
        Func<CollectedSourceReference, CancellationToken, Task<OperationResult<DecodedPictureObservations>>> pictures)
    { this.probe = probe; this.pictures = pictures; }
    public bool HasUnconfirmedProcess(Guid operationId) { lock (gate) return unconfirmed.Contains(operationId); }

    public async Task<OperationResult<VerifiedClipOutput>> VerifyAsync(QualifiedClipPlan plan, MediaProcessingResult processing,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(processing);
        try { await operations.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<VerifiedClipOutput>.Cancelled(); }
        var locks = new List<FileStream>(); bool closeFailed = false;
        OperationResult<VerifiedClipOutput> result;
        try
        {
            lock (gate) if (unconfirmed.Count != 0) return OperationResult<VerifiedClipOutput>.Failed(new(
                OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "이전 검증 프로세스의 종료가 확인되지 않았습니다.", "PreviousVerificationTermination"));
            result = await VerifyCore(plan, processing, locks, token).ConfigureAwait(false);
            if (result.Error?.Code == OperationErrorCode.ProcessTerminationFailed) lock (gate) unconfirmed.Add(processing.Request.OperationId);
        }
        finally
        {
            foreach (var input in locks) try { await input.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { closeFailed = true; }
            operations.Release();
        }
        if (closeFailed && result.Error?.Code != OperationErrorCode.ProcessTerminationFailed) return Fail("AccurateVerificationClose");
        if (token.IsCancellationRequested && result.Error?.Code != OperationErrorCode.ProcessTerminationFailed)
            return OperationResult<VerifiedClipOutput>.Cancelled();
        return result;
    }

    private async Task<OperationResult<VerifiedClipOutput>> VerifyCore(QualifiedClipPlan plan, MediaProcessingResult processing,
        List<FileStream> locks, CancellationToken token)
    {
        if (plan.Processing.Mode != MediaProcessingMode.Accurate || !ReferenceEquals(plan.Processing, processing.Request.Plan) ||
            processing.Staging.Content is null || processing.Staging.Id != processing.Request.OutputFileId ||
            !string.Equals(processing.Staging.Path, processing.Request.StagingPath, StringComparison.OrdinalIgnoreCase)) return Fail("AccurateProcessedIdentity");
        var render = processing.Request.AudioRender;
        if (plan.Processing.AudioBoundary is null && render is not null) return Fail("UnexpectedAccurateRender");
        if (plan.Processing.AudioBoundary is { } boundary && (render is null || !ReferenceEquals(render.Boundary, boundary) ||
            render.DecodedPcm != plan.Processing.DecoderPreparation?.Value?.DecodedPcm)) return Fail("AccurateRenderIdentity");
        var files = plan.Processing.Inputs.Concat(render is null ? [] : new[] { render.DecodedPcm, render.RenderPcm }).Concat(
            new[] { new MediaInputFile(processing.Staging.Id, processing.Staging.Path, processing.Staging.Content) }).ToArray();
        try
        {
            foreach (var file in files)
            {
                var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                locks.Add(input);
                if (!await Matches(input, file.Content, token).ConfigureAwait(false)) return Fail("AccurateByteIdentityBefore");
            }
            PresentationRange.TryCreate(SignedMediaTime.Zero, plan.SourceTarget.End - plan.RenderOrigin, out var range, out _);
            var request = new MediaProbeRequest(Guid.NewGuid(), processing.Staging.Id, processing.Staging.Path, processing.Staging.Content,
                MediaProbeDetail.FramesAndPackets, range, true, MediaProbePurpose.Output);
            var probed = await probe.ProbeAsync(request, token).ConfigureAwait(false);
            if (probed.Status == MediaOperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
            if (probed.Status != MediaOperationStatus.Succeeded) return Failure(probed.Failure!);
            if (!ReferenceEquals(probed.Data!.Request, request)) return Fail("AccurateOutputProbeIdentity");
            var observed = new Mp4OutputObservationVerifier().Verify(probed.Data);
            if (observed.Status != MediaOperationStatus.Succeeded) return Failure(observed.Failure!);
            if (CheckTiming(plan, observed.Data!) is { } field) return Fail(field);
            var outputFile = new OwnedMediaFile(processing.Staging.Id, processing.Staging.Path, ["local-output"], processing.Staging.Content);
            var decoded = await pictures(new LocalVerificationReference(outputFile, processing.Staging.Content), token).ConfigureAwait(false);
            if (decoded.Status == OperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
            if (decoded.Status != OperationStatus.Succeeded) return OperationResult<VerifiedClipOutput>.Failed(decoded.Error!);
            var selection = plan.Inspection.Acquisition.Request.Selection.Selection.VideoFormat;
            var expected = plan.Processing.RequiredVideoFrames.OrderBy(f => f.OutputPts).ToArray();
            if (decoded.Data!.Width != selection.Width || decoded.Data.Height != selection.Height || decoded.Data.Pictures.Count != expected.Length)
                return Fail("AccurateDecodedLayout");
            for (int i = 0; i < expected.Length; i++)
                if (Time(decoded.Data.Pictures[i].Pts, decoded.Data.Clock) != expected[i].OutputPts ||
                    Time(decoded.Data.Pictures[i].Duration, decoded.Data.Clock) != expected[i].OutputDuration) return Fail("AccurateDecodedMembership");
            for (int i = 0; i < files.Length; i++)
                if (!await Matches(locks[i], files[i].Content, token).ConfigureAwait(false)) return Fail("AccurateByteIdentityAfter");
            var output = observed.Data!;
            var video = output.Streams.Single(s => s.Stream.Kind == MediaStreamKind.Video);
            PresentationRange.TryCreate(video.ValidPresentation.Start + plan.RenderOrigin, video.ValidPresentation.End + plan.RenderOrigin, out var videoSource, out _);
            PresentationRange? audioSource = null;
            if (output.Streams.SingleOrDefault(s => s.Stream.Kind == MediaStreamKind.Audio) is { } audio)
                PresentationRange.TryCreate(audio.ValidPresentation.Start + plan.Processing.AudioBoundary!.RenderOrigin,
                    audio.ValidPresentation.End + plan.Processing.AudioBoundary.RenderOrigin, out audioSource, out _);
            token.ThrowIfCancellationRequested();
            return OperationResult<VerifiedClipOutput>.Succeeded(new(plan, processing, output, videoSource!, audioSource));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<VerifiedClipOutput>.Cancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("AccurateVerificationRead"); }
    }

    internal static string? CheckTiming(QualifiedClipPlan plan, Mp4OutputObservation output)
    {
        if (output.Streams.Count != plan.Processing.Streams.Count) return "AccurateRequiredStreams";
        var video = output.Streams.SingleOrDefault(s => s.Stream.Kind == MediaStreamKind.Video);
        if (video?.Configuration.Codec != "h264" || video.Stream.Codec.PixelFormat != "yuv420p") return "AccurateOutputCodec";
        var expected = plan.Processing.RequiredVideoFrames.OrderBy(f => f.OutputPts).ToArray();
        var frames = output.Probe.Observations.Frames.Where(f => f.StreamIndex == video.Stream.Index).OrderBy(f => f.Pts).ToArray();
        if (frames.Length != expected.Length) return "AccurateVideoMembership";
        for (int i = 0; i < frames.Length; i++)
            if (frames[i].Pts is not { } pts || frames[i].Duration is not { } duration ||
                Time(pts, video.Stream.TimeBase) != expected[i].OutputPts || Time(duration, video.Stream.TimeBase) != expected[i].OutputDuration)
                return "AccurateVideoMembership";
        if (video.ValidPresentation.Start != expected[0].OutputPts || video.ValidPresentation.End != expected.Max(f => f.OutputPts + f.OutputDuration))
            return "AccurateVideoPresentationEnd";
        var audio = output.Streams.SingleOrDefault(s => s.Stream.Kind == MediaStreamKind.Audio);
        if (plan.Processing.AudioBoundary is { } boundary)
        {
            var stream = plan.Processing.Streams.Single(s => s.Stream.Kind == MediaStreamKind.Audio);
            if (audio?.Configuration.Codec != "aac" || audio.Stream.Codec.Profile != "LC" ||
                audio.Configuration.SampleRate != boundary.Lattice.SampleRate || audio.Configuration.Channels != stream.Channels)
                return "AccurateAudioCodec";
            if (audio.ValidPresentation.Start != Time(boundary.SelectedFirst - boundary.RequestedFirst, Time(1, boundary.Lattice.SampleRate)) ||
                audio.ValidPresentation.End != Time(boundary.SelectedEnd - boundary.RequestedFirst, Time(1, boundary.Lattice.SampleRate)))
                return "AccurateAudioPresentationEnd";
        }
        else if (audio is not null) return "UnexpectedAccurateAudio";
        return null;
    }
    private static SignedMediaTime Time(BigInteger ticks, SignedMediaTime clock) => Time(ticks * clock.Numerator, clock.Denominator);
    private static SignedMediaTime Time(BigInteger n, BigInteger d) { SignedMediaTime.TryCreate(n, d, out var value, out _); return value!; }
    private static async Task<bool> Matches(FileStream file, MediaFileContentIdentity content, CancellationToken token)
    { if (file.Length != content.Bytes) return false; file.Position = 0; return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(content.Sha256, StringComparison.OrdinalIgnoreCase) && file.Length == content.Bytes; }
    private static OperationResult<VerifiedClipOutput> Fail(string field) => OperationResult<VerifiedClipOutput>.Failed(new(
        OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "Accurate 출력의 sample 경계·영상 membership·실제 표시 끝을 확인하지 못했습니다.", field));
    private static OperationResult<VerifiedClipOutput> Failure(MediaFailure failure) => OperationResult<VerifiedClipOutput>.Failed(new(
        failure.Kind == MediaFailureKind.TerminationUnconfirmed ? OperationErrorCode.ProcessTerminationFailed :
        failure.Kind == MediaFailureKind.MissingTool ? OperationErrorCode.MissingTool : failure.Kind == MediaFailureKind.TimedOut ? OperationErrorCode.TimedOut :
        failure.Kind == MediaFailureKind.ProcessFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed,
        OperationErrorCategory.Execution, failure.Message, failure.Field));
}
