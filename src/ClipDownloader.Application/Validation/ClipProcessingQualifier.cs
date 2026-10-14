using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Validation;

public sealed class QualifiedClipPlan
{
    public AcquiredMediaInspection Inspection { get; }
    public PresentationRange SourceTarget { get; }
    public SignedMediaTime RenderOrigin { get; }
    public MediaProcessingPlan Processing { get; }
    internal QualifiedClipPlan(AcquiredMediaInspection inspection, PresentationRange target,
        SignedMediaTime renderOrigin, MediaProcessingPlan processing)
    { Inspection = inspection; SourceTarget = target; RenderOrigin = renderOrigin; Processing = processing; }
}

// Pure qualification; verified facts must be produced by actual scoped inspection.
public sealed class ClipProcessingQualifier
{
    public OperationResult<QualifiedClipPlan> Qualify(AcquiredMediaInspection inspection,
        MediaInspectionFact<MediaRandomAccessPoint>? randomAccess = null,
        MediaInspectionFact<AudioDecoderPreparation>? decoderPreparation = null)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var acquired = inspection.Acquisition;
        var timeline = acquired.Request.Inspection.Timeline;
        if (timeline.Origin.State != MediaInspectionState.Verified) return Fail("OriginUnverified");
        if (timeline.SharedAxis.State != MediaInspectionState.Verified) return Fail("SharedAxisUnverified");
        if (timeline.AudioPresence == StreamPresence.Unknown) return Fail("AudioPresenceUnverified");
        var videos = inspection.Streams.Where(s => s.Native.Stream.Kind == MediaStreamKind.Video).ToArray();
        var audios = inspection.Streams.Where(s => s.Native.Stream.Kind == MediaStreamKind.Audio).ToArray();
        if (videos.Length != 1 || audios.Length != (timeline.AudioPresence == StreamPresence.Present ? 1 : 0))
            return Fail("RequiredStreamMissing");
        if (videos[0].Native.Configuration.Codec != "h264" || audios.Any(a => a.Native.Configuration.Codec != "aac"))
            return Fail("UnsupportedStrategy", unsupported: true);
        var origin = timeline.Origin.Value!;
        var start = origin + SignedMediaTime.FromTimecode(acquired.Request.Range.Start);
        var end = origin + SignedMediaTime.FromTimecode(acquired.Request.Range.End);
        PresentationRange.TryCreate(start, end, out var target, out _);
        var video = videos[0];
        var videoOffset = video.Mapping.SourceOffset.Value!;
        var videoReference = video.Mapping.SourceOffset.Scope.Stream;
        if (!video.Mapping.Coverage.Scope.Covers(acquired.AcquisitionId, videoReference, MediaInspectionKind.Frames, MediaTimeBasis.Native, target!) ||
            !video.Mapping.SourceOffset.Scope.Covers(acquired.AcquisitionId, videoReference, MediaInspectionKind.Frames, MediaTimeBasis.Native, target!))
            return Fail("CoverageIncomplete");
        var mode = acquired.Request.Selection.Mode == ClipMode.Fast ? MediaProcessingMode.Fast : MediaProcessingMode.Accurate;
        if (mode == MediaProcessingMode.Accurate && video.Native.Stream.Codec.PixelFormat != "yuv420p")
            return Fail("AccuratePixelFormatUnsupported", unsupported: true);
        if (inspection.Files.SelectMany(f => f.Probe.Observations.Packets).Any(p => p.Pts is null || p.Dts is null || p.Duration is null))
            return Fail("NativeTimingUnverified");
        if (mode == MediaProcessingMode.Fast && video.Probe.Observations.Packets.Any(p => p.StreamIndex == video.Native.Stream.Index && p.Pts < p.Dts))
            return Fail("FastCompositionTimeUnsupported", unsupported: true);
        SignedMediaTime renderOrigin = start;
        if (mode == MediaProcessingMode.Fast)
        {
            var stream = video.Mapping.SourceOffset.Scope.Stream;
            if (randomAccess?.State != MediaInspectionState.Verified || randomAccess.Value is null ||
                randomAccess.Scope.SnapshotId != acquired.AcquisitionId || randomAccess.Scope.Stream != stream ||
                randomAccess.Scope.TimeBasis != MediaTimeBasis.Native || randomAccess.Scope.Kind != MediaInspectionKind.Frames ||
                randomAccess.Value.Timestamp.TimeBase != video.Native.Stream.TimeBase ||
                randomAccess.Value.PicturePayloadSha256.Length != 64 || !randomAccess.Value.PicturePayloadSha256.All(char.IsAsciiHexDigit))
                return Fail("KeyframeUnverified");
            var point = randomAccess.Value.Timestamp;
            var packet = video.Probe.Observations.Packets.SingleOrDefault(p => p.StreamIndex == stream.StreamIndex && p.Pts == point.Pts);
            if (packet?.Flags is null || !packet.Flags.Contains('K') || packet.Duration is not > 0 ||
                randomAccess.Scope.Range is null || randomAccess.Scope.Range.Start.CompareTo(point.PresentationTime) > 0 ||
                randomAccess.Scope.Range.End.CompareTo(point.PresentationTime) <= 0)
                return Fail("KeyframeUnverified");
            renderOrigin = point.PresentationTime + videoOffset;
            if (renderOrigin.CompareTo(start) > 0) return Fail("KeyframeAfterIn");
            PresentationRange.TryCreate(renderOrigin, renderOrigin + randomAccess.Scope.Range.End - randomAccess.Scope.Range.Start, out var keySourceRange, out _);
            if (!video.Mapping.SourceOffset.Scope.Covers(acquired.AcquisitionId, stream, MediaInspectionKind.Frames,
                MediaTimeBasis.Native, keySourceRange!)) return Fail("KeyframeMappingUnverified");
        }
        var frames = new List<MediaVideoFrameExpectation>();
        foreach (var frame in video.Probe.Observations.Frames.Where(f => f.StreamIndex == video.Native.Stream.Index))
        {
            if (frame.Pts is not { } pts || frame.Duration is not > 0) return Fail("VideoTimingUnverified");
            SignedMediaTime.TryCreate((BigInteger)pts * video.Native.Stream.TimeBase.Numerator,
                video.Native.Stream.TimeBase.Denominator, out var local, out _);
            var source = local! + videoOffset;
            if (source.CompareTo(start) < 0 || source.CompareTo(end) >= 0) continue;
            SignedMediaTime.TryCreate((BigInteger)frame.Duration.Value * video.Native.Stream.TimeBase.Numerator,
                video.Native.Stream.TimeBase.Denominator, out var duration, out _);
            frames.Add(new(pts, frame.Duration.Value, video.Native.Stream.TimeBase, source - renderOrigin, duration!));
        }
        if (frames.Count == 0) return Fail("EmptyVideoSelection", unsupported: true);
        if (frames.Select(f => f.InputPts).Distinct().Count() != frames.Count) return Fail("VideoTimingContradicted");
        var plans = new List<StreamProcessingPlan>();
        PresentationRange.TryCreate((mode == MediaProcessingMode.Fast ? renderOrigin : start) - videoOffset, end - videoOffset, out var videoRange, out _);
        bool knownLeadingVideo = inspection.Files.Single(f => f.Probe.Request.FileId == videoReference.FileId).SourceStart is not null;
        if (videoRange is null || video.Native.ObservedRange.Start.CompareTo(videoRange.Start) > 0 && !knownLeadingVideo ||
            video.Native.ObservedRange.End.CompareTo(videoRange.End) < 0) return Fail("CoverageIncomplete");
        plans.Add(StreamPlan(video, videoRange, videoOffset - renderOrigin));
        AudioBoundaryPlan? boundary = null;
        if (audios.Length == 1)
        {
            var audio = audios[0];
            var offset = audio.Mapping.SourceOffset.Value! + timeline.SharedAxis.Value!.AudioToVideoOffset;
            if (audio.Native.Stream.Codec.SampleRate is not { } rate) return Fail("AudioLatticeUnverified");
            if (mode == MediaProcessingMode.Fast && audio.Probe.Observations.Packets.Any(p => p.StreamIndex == audio.Native.Stream.Index && p.Pts != p.Dts))
                return Fail("FastAudioCompositionUnsupported", unsupported: true);
            var lattice = new AudioSampleLattice(audio.Native.ObservedRange.Start + offset, rate);
            var supportStart = lattice.Origin;
            var supportEnd = audio.Native.ObservedRange.End + offset;
            if (supportEnd.CompareTo(start) <= 0 || supportStart.CompareTo(end) >= 0)
                return Fail("EmptyRequiredAudioSelection", unsupported: true);
            if (supportEnd.CompareTo(end) < 0) return Fail("AudioSupportEndUnverified");
            var selectedStart = start.CompareTo(supportStart) > 0 ? start : supportStart;
            var axisOffset = timeline.SharedAxis.Value!.AudioToVideoOffset;
            PresentationRange.TryCreate(selectedStart - axisOffset, end - axisOffset, out var neededAudioSource, out _);
            var audioReference = audio.Mapping.SourceOffset.Scope.Stream;
            if (!audio.Mapping.Coverage.Scope.Covers(acquired.AcquisitionId, audioReference, MediaInspectionKind.Samples, MediaTimeBasis.Native, neededAudioSource!) ||
                !audio.Mapping.SourceOffset.Scope.Covers(acquired.AcquisitionId, audioReference, MediaInspectionKind.Samples, MediaTimeBasis.Native, neededAudioSource!))
                return Fail("CoverageIncomplete");
            SignedMediaTime audioRender = renderOrigin;
            if (mode == MediaProcessingMode.Accurate)
            {
                if (!lattice.TryGetSampleIndex(start, NativeTimestampRounding.Ceiling, out long first, out _) ||
                    !lattice.TryGetSampleIndex(end, NativeTimestampRounding.Ceiling, out long last, out _) ||
                    !lattice.TryGetSampleIndex(supportEnd, NativeTimestampRounding.Exact, out long supportLast, out _))
                    return Fail("AudioBoundaryUnsupported", unsupported: true);
                boundary = new(lattice, first, last, 0, supportLast, start);
                audioRender = boundary.RenderOrigin;
                if (decoderPreparation?.State != MediaInspectionState.Verified || decoderPreparation.Value is null ||
                    !decoderPreparation.Scope.Covers(acquired.AcquisitionId, audio.Mapping.SourceOffset.Scope.Stream,
                        MediaInspectionKind.Samples, MediaTimeBasis.Native, audio.Mapping.Coverage.Value!))
                    return Fail("DecoderPreparationUnverified");
                var preparation = decoderPreparation.Value;
                var sourceStart = acquired.Request.Inspection.Files.SingleOrDefault(f => f.Id == preparation.SourceStartFileId);
                var audioFile = acquired.Files.Single(f => f.Id == audio.Mapping.SourceOffset.Scope.Stream.FileId);
                var sourcePrefix = inspection.Files.Single(f => f.Probe.Request.FileId == audioFile.Id).SourceStart;
                var decodedSamples = audio.Probe.Observations.Frames.Where(f => f.StreamIndex == audio.Native.Stream.Index)
                    .Aggregate(BigInteger.Zero, (sum, f) => sum + (f.SampleCount ?? 0));
                if (sourceStart?.Content is null || preparation.SourceStartContent is null ||
                    sourceStart.Content != preparation.SourceStartContent || preparation.SourceStartFileId == Guid.Empty ||
                    !sourceStart.SourceFormatIds.SequenceEqual(audioFile.SourceFormatIds) ||
                    sourcePrefix is null || sourcePrefix.SourceFirstFileId != preparation.SourceStartFileId ||
                    sourcePrefix.SourceFirstContent != preparation.SourceStartContent ||
                    preparation.DecodedPcm?.Content is null ||
                    preparation.SampleRate != rate || preparation.Channels != audio.Native.Stream.Codec.Channels ||
                    preparation.Samples <= 0 || preparation.Samples != decodedSamples ||
                    preparation.DecodedPcm.Content.Bytes != (BigInteger)preparation.Samples * preparation.Channels * 4 ||
                    preparation.DecodedPcm.Id == Guid.Empty || !Path.IsPathFullyQualified(preparation.DecodedPcm.Path) ||
                    !AcquisitionContractValidation.SameDirectory(Path.GetDirectoryName(preparation.DecodedPcm.Path)!, acquired.Request.WorkDirectory))
                    return Fail("DecoderPreparationIdentity");
            }
            else if (!(offset - renderOrigin).TryToNativeTicks(audio.Native.Stream.TimeBase, NativeTimestampRounding.Exact, out _, out _))
                return Fail("FastAudioGridUnsupported", unsupported: true);
            var audioStart = mode == MediaProcessingMode.Fast ? renderOrigin : boundary!.Lattice.GetSampleTime(boundary.SelectedFirst);
            if (audioStart.CompareTo(supportStart) < 0) audioStart = supportStart;
            var audioEnd = mode == MediaProcessingMode.Accurate ? boundary!.Lattice.GetSampleTime(boundary.SelectedEnd) :
                end.CompareTo(supportEnd) < 0 ? end : supportEnd;
            PresentationRange.TryCreate(audioStart - offset, audioEnd - offset, out var audioRange, out _);
            if (audioRange is null) return Fail("EmptyRequiredAudioSelection", unsupported: true);
            plans.Add(StreamPlan(audio, audioRange, offset - audioRender));
        }
        BigInteger clock = 1;
        foreach (var time in plans.SelectMany(p => new[] { p.Translation, p.InputRange.Start, p.InputRange.End })
            .Concat(inspection.Streams.Select(s => s.Native.Stream.TimeBase)))
        {
            clock = clock / BigInteger.GreatestCommonDivisor(clock, time.Denominator) * time.Denominator;
            if (clock > int.MaxValue) return Fail("MovieClockUnsupported", unsupported: true);
        }
        var inputs = acquired.Files.Select(f => new MediaInputFile(f.Id, f.Path, f.Content!));
        var processing = new MediaProcessingPlan(mode, inputs, plans, frames.OrderBy(f => f.InputPts), (int)clock, boundary, decoderPreparation);
        return OperationResult<QualifiedClipPlan>.Succeeded(new(inspection, target!, renderOrigin, processing));
    }

    private static StreamProcessingPlan StreamPlan(AcquiredStreamInspection stream, PresentationRange range, SignedMediaTime translation)
    {
        var ticks = stream.Probe.Observations.Packets.Where(p => p.StreamIndex == stream.Native.Stream.Index)
            .SelectMany(p => new[] { new BigInteger(p.Pts!.Value), new BigInteger(p.Dts!.Value),
                new BigInteger(p.Pts.Value) + p.Duration!.Value, new BigInteger(p.Dts.Value) + p.Duration.Value }).ToArray();
        return new(stream.Mapping.SourceOffset.Scope.Stream, range, translation, stream.Native.Configuration.Codec,
            stream.Native.Stream.TimeBase, ticks.Min(), ticks.Max(), stream.Native.Stream.Codec.SampleRate, stream.Native.Stream.Codec.Channels);
    }

    private static OperationResult<QualifiedClipPlan> Fail(string field, bool unsupported = false) => OperationResult<QualifiedClipPlan>.Failed(
        new(unsupported ? OperationErrorCode.UnsupportedFormat : OperationErrorCode.VerificationFailed,
            OperationErrorCategory.Execution, "선택 source의 시간·support·준비 근거로 처리 계획을 확정하지 못했습니다.", field));
}
