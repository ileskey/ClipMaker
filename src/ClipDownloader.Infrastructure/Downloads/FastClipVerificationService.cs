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

internal sealed class LocalVerificationReference(OwnedMediaFile file, MediaFileContentIdentity content) :
    CollectedSourceReference(file, content.Bytes, content.Bytes, content.Sha256);

public sealed class FastClipVerificationService
{
    private readonly IMediaProbe probe;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object gate = new();
    private readonly HashSet<Guid> unconfirmed = [];
    private readonly Func<CollectedSourceReference, CancellationToken, Task<OperationResult<DecodedPictureObservations>>> pictures;
    public FastClipVerificationService(IMediaProbe probe, IToolLocator locator, ToolPathSettings settings, IProcessRunner runner)
    {
        this.probe = probe;
        pictures = new PresentationVideoProbe(locator, settings, runner).DecodeAsync;
    }
    internal FastClipVerificationService(IMediaProbe probe,
        Func<CollectedSourceReference, CancellationToken, Task<OperationResult<DecodedPictureObservations>>> pictures)
    { this.probe = probe; this.pictures = pictures; }

    public async Task<OperationResult<VerifiedClipOutput>> VerifyAsync(QualifiedClipPlan plan, MediaProcessingResult processing, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(processing);
        try { await operations.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<VerifiedClipOutput>.Cancelled(); }
        var locks = new List<FileStream>();
        OperationResult<VerifiedClipOutput> outcome;
        bool closeFailed = false;
        try
        {
            lock (gate)
                if (unconfirmed.Count != 0) return OperationResult<VerifiedClipOutput>.Failed(new(
                    OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution,
                    "이전 출력 검증 프로세스의 종료가 확인되지 않았습니다.", "PreviousVerificationTermination"));
            outcome = await VerifyCoreAsync(plan, processing, locks, token).ConfigureAwait(false);
            if (outcome.Error?.Code == OperationErrorCode.ProcessTerminationFailed)
                lock (gate) unconfirmed.Add(processing.Request.OperationId);
        }
        finally
        {
            foreach (var input in locks)
                try { await input.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { closeFailed = true; }
            operations.Release();
        }
        if (closeFailed && outcome.Error?.Code != OperationErrorCode.ProcessTerminationFailed) return Fail("VerificationReadClose");
        if (token.IsCancellationRequested && outcome.Error?.Code != OperationErrorCode.ProcessTerminationFailed)
            return OperationResult<VerifiedClipOutput>.Cancelled();
        return outcome;
    }

    // The verifier creates no files. The caller must preserve its input/staging
    // resources while a probe or decoder termination remains unconfirmed.
    public bool HasUnconfirmedProcess(Guid operationId) { lock (gate) return unconfirmed.Contains(operationId); }

    private async Task<OperationResult<VerifiedClipOutput>> VerifyCoreAsync(QualifiedClipPlan plan, MediaProcessingResult processing,
        List<FileStream> locks, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(processing);
        if (plan.Processing.Mode != MediaProcessingMode.Fast || !ReferenceEquals(plan.Processing, processing.Request.Plan) ||
            processing.Staging.Content is null || processing.Staging.Id != processing.Request.OutputFileId ||
            processing.Staging.Path != processing.Request.StagingPath) return Fail("ProcessedPlanIdentity");
        if (token.IsCancellationRequested) return OperationResult<VerifiedClipOutput>.Cancelled();
        PresentationRange.TryCreate(SignedMediaTime.Zero, plan.SourceTarget.End - plan.RenderOrigin, out var requested, out _);
        var request = new MediaProbeRequest(Guid.NewGuid(), processing.Staging.Id, processing.Staging.Path, processing.Staging.Content,
            MediaProbeDetail.FramesAndPackets, requested!, true, MediaProbePurpose.Output);
        var inspected = await probe.ProbeAsync(request, token).ConfigureAwait(false);
        if (inspected.Status == MediaOperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
        if (inspected.Status != MediaOperationStatus.Succeeded) return Fail(inspected.Failure!);
        if (!ReferenceEquals(inspected.Data!.Request, request)) return Fail("OutputProbeIdentity");
        var verified = new Mp4OutputObservationVerifier().Verify(inspected.Data);
        if (verified.Status != MediaOperationStatus.Succeeded) return Fail(verified.Failure!);
        var output = verified.Data!;
        if (output.Streams.Count != plan.Processing.Streams.Count) return Fail("OutputRequiredStreams");
        try
        {
            var opened = new Dictionary<Guid, FileStream>();
            foreach (var file in plan.Processing.Inputs.Concat(new[] { new MediaInputFile(processing.Staging.Id, processing.Staging.Path, processing.Staging.Content) }))
            {
                var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                locks.Add(input); opened.Add(file.Id, input);
                if (!await Matches(input, file.Content, token).ConfigureAwait(false)) return Fail("VerificationByteIdentityBefore");
            }
            foreach (var streamPlan in plan.Processing.Streams)
            {
                var source = plan.Inspection.Streams.Single(s => s.Mapping.SourceOffset.Scope.Stream == streamPlan.Stream);
                var destination = output.Streams.SingleOrDefault(s => s.Stream.Kind == streamPlan.Stream.Kind);
                if (destination is null || destination.Configuration.Codec != source.Native.Configuration.Codec ||
                    !string.Equals(destination.Configuration.ConfigSha256, source.Native.Configuration.ConfigSha256, StringComparison.OrdinalIgnoreCase) ||
                    destination.Stream.Codec.Profile != source.Native.Stream.Codec.Profile ||
                    destination.Stream.Codec.PixelFormat != source.Native.Stream.Codec.PixelFormat ||
                    destination.Stream.Codec.SampleRate != source.Native.Stream.Codec.SampleRate || destination.Stream.Codec.Channels != source.Native.Stream.Codec.Channels)
                    return Fail("CopyCodecInitializationChanged");
                var packets = source.Probe.Observations.Packets.Where(p => p.StreamIndex == source.Native.Stream.Index).ToArray();
                var matched = new HashSet<int>();
                int previousSourceIndex = -1;
                for (int outputIndex = 0; outputIndex < destination.Samples.Count; outputIndex++)
                {
                    var sample = destination.Samples[outputIndex];
                    var sourcePts = Time(sample.Pts, destination.Stream.TimeBase) - streamPlan.Translation;
                    var sourceDts = Time(sample.Dts, destination.Stream.TimeBase) - streamPlan.Translation;
                    if (!sourcePts.TryToNativeTicks(source.Native.Stream.TimeBase, NativeTimestampRounding.Exact, out long pts, out _) ||
                        !sourceDts.TryToNativeTicks(source.Native.Stream.TimeBase, NativeTimestampRounding.Exact, out long dts, out _))
                        return Fail("CopyTimestampChanged");
                    var candidates = Enumerable.Range(0, packets.Length).Where(i => packets[i].Pts == pts && packets[i].Dts == dts).ToArray();
                    if (candidates.Length != 1 || !matched.Add(candidates[0])) return Fail("CopyPacketAmbiguous");
                    var packet = packets[candidates[0]];
                    bool isAudio = streamPlan.Stream.Kind == MediaStreamKind.Audio;
                    if (isAudio && previousSourceIndex >= 0 && candidates[0] != previousSourceIndex + 1)
                        return Fail("CopyAudioPacketSequenceChanged");
                    previousSourceIndex = candidates[0];
                    // Progressive MP4 STTS describes the distance to the next
                    // coded DTS, whereas AAC decoded support remains 1024
                    // samples. Verify the exact source DTS difference; never
                    // allow an arbitrary duration tolerance or altered payload.
                    var expectedDuration = ExpectedCopyDuration(packet,
                        isAudio && outputIndex + 1 < destination.Samples.Count && candidates[0] + 1 < packets.Length
                            ? packets[candidates[0] + 1] : null, isAudio, source.Native.Stream.TimeBase);
                    if (packet.Size != sample.Native.Size || packet.Position is null || packet.Duration is null ||
                        expectedDuration != Time(sample.Native.Duration, destination.Stream.TimeBase))
                        return Fail("CopyPacketChanged");
                    var sourceHash = await HashRange(opened[streamPlan.Stream.FileId], packet.Position.Value, sample.Native.Size, token).ConfigureAwait(false);
                    var targetHash = await HashRange(opened[processing.Staging.Id], sample.Native.ByteOffset, sample.Native.Size, token).ConfigureAwait(false);
                    if (!sourceHash.AsSpan().SequenceEqual(targetHash)) return Fail("CopyPacketPayloadChanged");
                }
                if (streamPlan.Stream.Kind == MediaStreamKind.Audio)
                {
                    var offset = source.Mapping.SourceOffset.Value! + plan.Inspection.Acquisition.Request.Inspection.Timeline.SharedAxis.Value!.AudioToVideoOffset;
                    for (int i = 0; i < packets.Length; i++)
                    {
                        var packet = packets[i];
                        var start = Time(packet.Pts!.Value, source.Native.Stream.TimeBase) + offset;
                        var end = start + Time(packet.Duration!.Value, source.Native.Stream.TimeBase);
                        if (start.CompareTo(plan.SourceTarget.End) < 0 && end.CompareTo(plan.SourceTarget.Start) > 0 && !matched.Contains(i))
                            return Fail("RequiredAudioPacketMissing");
                    }
                }
            }
            var videoOutput = output.Streams.Single(s => s.Stream.Kind == MediaStreamKind.Video);
            var frames = output.Probe.Observations.Frames.Where(f => f.StreamIndex == videoOutput.Stream.Index).ToArray();
            foreach (var expected in plan.Processing.RequiredVideoFrames)
                if (!frames.Any(f => f.Pts is { } pts && Time(pts, videoOutput.Stream.TimeBase) == expected.OutputPts &&
                    f.Duration is { } duration && Time(duration, videoOutput.Stream.TimeBase) == expected.OutputDuration))
                    return Fail("RequiredVideoFrameMissing");
            var sourceVideo = plan.Inspection.Streams.Single(s => s.Native.Stream.Kind == MediaStreamKind.Video);
            var sourceFile = plan.Inspection.Acquisition.Files.Single(f => f.Id == sourceVideo.Probe.Request.FileId);
            var sourcePictures = await pictures(new LocalVerificationReference(sourceFile, sourceFile.Content!), token).ConfigureAwait(false);
            if (sourcePictures.Status == OperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
            if (sourcePictures.Status != OperationStatus.Succeeded) return OperationResult<VerifiedClipOutput>.Failed(sourcePictures.Error!);
            var outputFile = new OwnedMediaFile(processing.Staging.Id, processing.Staging.Path, ["local-output"], processing.Staging.Content);
            var outputPictures = await pictures(new LocalVerificationReference(outputFile, processing.Staging.Content), token).ConfigureAwait(false);
            if (outputPictures.Status == OperationStatus.Cancelled) return OperationResult<VerifiedClipOutput>.Cancelled();
            if (outputPictures.Status != OperationStatus.Succeeded) return OperationResult<VerifiedClipOutput>.Failed(outputPictures.Error!);
            var sourceDecoded = sourcePictures.Data!; var targetDecoded = outputPictures.Data!;
            if (sourceDecoded.Width != targetDecoded.Width || sourceDecoded.Height != targetDecoded.Height ||
                sourceDecoded.Width != plan.Inspection.Acquisition.Request.Selection.Selection.VideoFormat.Width ||
                sourceDecoded.Height != plan.Inspection.Acquisition.Request.Selection.Selection.VideoFormat.Height ||
                sourceVideo.Native.Stream.Codec.PixelFormat != "yuv420p" || targetDecoded.Pictures.Count != frames.Length)
                return Fail("CopyDecodedPictureLayout");
            var translation = plan.Processing.Streams.Single(s => s.Stream.Kind == MediaStreamKind.Video).Translation;
            foreach (var picture in targetDecoded.Pictures)
            {
                var localSource = Time(picture.Pts, targetDecoded.Clock) - translation;
                var matches = sourceDecoded.Pictures.Where(p => Time(p.Pts, sourceDecoded.Clock) == localSource).ToArray();
                if (matches.Length != 1 || matches[0].Sha256 != picture.Sha256 ||
                    Time(matches[0].Duration, sourceDecoded.Clock) != Time(picture.Duration, targetDecoded.Clock))
                    return Fail("CopyDecodedPictureChanged");
            }
            foreach (var file in plan.Processing.Inputs.Concat(new[] { new MediaInputFile(processing.Staging.Id, processing.Staging.Path, processing.Staging.Content) }))
                if (!await Matches(opened[file.Id], file.Content, token).ConfigureAwait(false)) return Fail("VerificationByteIdentityAfter");
            token.ThrowIfCancellationRequested();
            PresentationRange.TryCreate(videoOutput.ValidPresentation.Start + plan.RenderOrigin,
                videoOutput.ValidPresentation.End + plan.RenderOrigin, out var videoSource, out _);
            PresentationRange? audioSource = null;
            if (output.Streams.SingleOrDefault(s => s.Stream.Kind == MediaStreamKind.Audio) is { } audio)
                PresentationRange.TryCreate(audio.ValidPresentation.Start + plan.RenderOrigin, audio.ValidPresentation.End + plan.RenderOrigin, out audioSource, out _);
            return OperationResult<VerifiedClipOutput>.Succeeded(new(plan, processing, output, videoSource!, audioSource));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<VerifiedClipOutput>.Cancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("CopyVerificationRead"); }
    }

    internal static SignedMediaTime? ExpectedCopyDuration(MediaPacketObservation packet, MediaPacketObservation? next,
        bool audio, SignedMediaTime clock)
    {
        if (packet.Duration is null) return null;
        if (!audio || next is null) return Time(packet.Duration.Value, clock);
        if (packet.Dts is null || next.Dts is null || next.Dts <= packet.Dts) return null;
        return Time(new BigInteger(next.Dts.Value) - packet.Dts.Value, clock);
    }

    private static SignedMediaTime Time(BigInteger pts, SignedMediaTime clock)
    { SignedMediaTime.TryCreate(pts * clock.Numerator, clock.Denominator, out var time, out _); return time!; }
    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity identity, CancellationToken token)
    {
        if (input.Length != identity.Bytes) return false;
        input.Position = 0;
        var hash = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return input.Length == identity.Bytes && Convert.ToHexString(hash).Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static async Task<byte[]> HashRange(FileStream input, long start, uint size, CancellationToken token)
    {
        if (start < 0 || size == 0 || start > input.Length - size) throw new InvalidDataException();
        input.Position = start; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long remaining = size;
        while (remaining > 0)
        {
            int count = (int)Math.Min(buffer.Length, remaining);
            await input.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            hash.AppendData(buffer, 0, count); remaining -= count;
        }
        return hash.GetHashAndReset();
    }
    private static OperationResult<VerifiedClipOutput> Fail(MediaFailure failure) => OperationResult<VerifiedClipOutput>.Failed(new(
        failure.Kind == MediaFailureKind.TerminationUnconfirmed ? OperationErrorCode.ProcessTerminationFailed :
        failure.Kind == MediaFailureKind.MissingTool ? OperationErrorCode.MissingTool :
        failure.Kind == MediaFailureKind.ProcessFailed ? OperationErrorCode.ProcessFailed :
        failure.Kind == MediaFailureKind.TimedOut ? OperationErrorCode.TimedOut : OperationErrorCode.VerificationFailed,
        OperationErrorCategory.Execution, failure.Message, failure.Field));
    private static OperationResult<VerifiedClipOutput> Fail(string field) => OperationResult<VerifiedClipOutput>.Failed(new(
        OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "Fast 출력의 원본 대응·필수 coverage·decoded 영상·표시 끝을 확인하지 못했습니다.", field));
}
