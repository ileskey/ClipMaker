using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record VerifiedNativeStream(InitialStreamObservation Stream, Mp4TrackConfiguration Configuration,
    PresentationRange ObservedRange);
internal sealed class VerifiedHlsNativeReference(ProbedInitialReference reference, IEnumerable<VerifiedNativeStream> streams)
{
    internal ProbedInitialReference Reference { get; } = reference;
    internal IReadOnlyList<VerifiedNativeStream> Streams { get; } = Array.AsReadOnly(streams.ToArray());
    // Only valid when this is the first whole source reference; the caller establishes that provenance.
    internal SignedMediaTime EarliestPresentation => Streams.Select(s => s.ObservedRange.Start).Min()!;
}

internal sealed class HlsNativeReferenceVerifier
{
    internal OperationResult<VerifiedHlsNativeReference> Verify(ProbedInitialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.NativeSamples is null) return Fail(OperationErrorCode.VerificationFailed, "NativeSamplesMissing");
        if (reference.NativeSamples.Status != OperationStatus.Succeeded)
            return OperationResult<VerifiedHlsNativeReference>.Failed(reference.NativeSamples.Error!);
        bool indexed = reference.Reference is IndexedCollectedReference;
        if (!reference.Observations.PacketsObserved || !indexed && reference.ContainerTiming.Tracks.Any(track => track.HasEditList))
            return Fail(OperationErrorCode.UnsupportedFormat, "NativeEditsUnsupported");
        var evidence = reference.NativeSamples.Data!;
        if (reference.Observations.Streams.Count != evidence.Configurations.Count ||
            (!indexed && reference.Observations.Streams.Count(s => s.Kind == MediaStreamKind.Video) != 1) ||
            (indexed && reference.Observations.Streams.Count != 1) ||
            reference.Observations.Streams.Count(s => s.Kind == MediaStreamKind.Audio) > 1)
            return Fail(OperationErrorCode.UnsupportedFormat, "NativeTrackLayout");
        var verified = new List<VerifiedNativeStream>();
        foreach (var stream in reference.Observations.Streams)
        {
            var track = reference.ContainerTiming.Tracks.SingleOrDefault(track => track.TrackId == stream.TrackId);
            if (track is null || stream.TimeBase.Numerator != 1 || stream.TimeBase.Denominator != track.MediaTimescale)
                return Fail(OperationErrorCode.VerificationFailed, "NativeTrackClock");
            BigInteger translation = 0;
            SignedMediaTime? movieEnd = null;
            if (track.HasEditList)
            {
                if (!indexed || track.Edits.Count != 1 || track.Edits[0] is not { MediaTime: >= 0, MovieDuration: > 0, RateInteger: 1, RateFraction: 0 } edit)
                    return Fail(OperationErrorCode.UnsupportedFormat, "NativeEditsUnsupported");
                translation = -new BigInteger(edit.MediaTime);
                SignedMediaTime.TryCreate(edit.MovieDuration, reference.ContainerTiming.MovieTimescale, out movieEnd, out _);
                if (movieEnd is null) return Fail(OperationErrorCode.VerificationFailed, "MovieClock");
            }
            var config = evidence.Configurations.SingleOrDefault(c => c.TrackId == stream.TrackId);
            if (config is null || config.Codec != stream.Codec.Name || config.ConfigBytes != stream.Codec.ExtradataSize)
                return Fail(OperationErrorCode.VerificationFailed, "NativeCodecIdentity");
            if (stream.Kind == MediaStreamKind.Video && config.Codec != "h264" || stream.Kind == MediaStreamKind.Audio &&
                (config.Codec != "aac" || stream.Codec.Profile != "LC" || stream.Codec.SampleRate != config.SampleRate ||
                    stream.Codec.Channels != config.Channels || stream.TimeBase.Denominator != config.SampleRate))
                return Fail(OperationErrorCode.UnsupportedFormat, "NativeCodecParameters");
            var samples = evidence.Samples.Where(s => s.TrackId == stream.TrackId).ToArray();
            var packets = reference.Observations.Packets.Where(p => p.StreamIndex == stream.Index).ToArray();
            var frames = reference.Observations.Frames.Where(f => f.StreamIndex == stream.Index).ToArray();
            if (samples.Length == 0 || samples.Length != packets.Length || samples.Length != frames.Length)
                return Fail(OperationErrorCode.VerificationFailed, "NativeSampleCompleteness");
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i]; var packet = packets[i];
                if (sample.PresentationTicks + translation < long.MinValue || sample.PresentationTicks + translation > long.MaxValue ||
                    sample.DecodeTicks + translation < long.MinValue || sample.DecodeTicks + translation > long.MaxValue)
                    return Fail(OperationErrorCode.UnsupportedFormat, "NativeClockOverflow");
                if (packet.Pts != (long)(sample.PresentationTicks + translation) || packet.Dts != (long)(sample.DecodeTicks + translation) ||
                    packet.Duration != sample.Duration || packet.Size != sample.Size || packet.Position != sample.ByteOffset)
                    return Fail(OperationErrorCode.VerificationFailed, "NativePacketTiming");
                if (packet.HasUninterpretedSideData || packet.Flags is null || packet.Flags.Contains('C') || packet.Flags.Contains('D'))
                    return Fail(OperationErrorCode.VerificationFailed, "NativePacketState");
                bool nativeSync = (sample.Flags & 0x01010000) == 0;
                if (nativeSync != packet.Flags.Contains('K'))
                    return Fail(OperationErrorCode.VerificationFailed, "NativeSyncState");
                if (packet.Skip is { } skip)
                {
                    if (skip.SkipSamples is null || skip.DiscardPadding is null || skip.SkipReason is null || skip.DiscardReason is null)
                        return Fail(OperationErrorCode.VerificationFailed, "NativeSkipMissing");
                    if (skip.SkipSamples != 0 || skip.DiscardPadding != 0 || skip.SkipReason != 0 || skip.DiscardReason != 0)
                        return Fail(OperationErrorCode.UnsupportedFormat, "NativeSkipUnsupported");
                }
            }
            var orderedSamples = samples.OrderBy(s => s.PresentationTicks).ToArray();
            if (frames.Any(f => f.Pts is null)) return Fail(OperationErrorCode.VerificationFailed, "NativeFramePtsMissing");
            var orderedFrames = frames.OrderBy(f => f.Pts).ToArray();
            for (int i = 0; i < orderedSamples.Length; i++)
            {
                var sample = orderedSamples[i]; var frame = orderedFrames[i];
                if (frame.Pts != (long)(sample.PresentationTicks + translation) || frame.Duration != sample.Duration)
                    return Fail(OperationErrorCode.VerificationFailed, "NativeFrameTiming");
                if (stream.Kind == MediaStreamKind.Audio && (frame.SampleCount != sample.Duration ||
                    frame.SampleRate is { } rate && rate != config.SampleRate || frame.Channels is { } channels && channels != config.Channels))
                    return Fail(OperationErrorCode.VerificationFailed, "NativeAudioSamples");
            }
            if (stream.StartPts != (long)(orderedSamples[0].PresentationTicks + translation))
                return Fail(OperationErrorCode.VerificationFailed, "NativeStartTiming");
            SignedMediaTime.TryCreate((orderedSamples[0].PresentationTicks + translation) * stream.TimeBase.Numerator,
                stream.TimeBase.Denominator, out var start, out _);
            BigInteger last = orderedSamples.Max(s => s.PresentationTicks + s.Duration) + translation;
            SignedMediaTime.TryCreate(last * stream.TimeBase.Numerator, stream.TimeBase.Denominator, out var end, out _);
            PresentationRange.TryCreate(start!, end!, out var range, out _);
            if (range is null) return Fail(OperationErrorCode.VerificationFailed, "NativeEmptyRange");
            if (movieEnd is not null && (range.Start.Numerator < 0 || range.End.CompareTo(movieEnd) > 0))
                return Fail(OperationErrorCode.UnsupportedFormat, "MovieEditClippingUnsupported");
            verified.Add(new(stream, config, range));
        }
        return OperationResult<VerifiedHlsNativeReference>.Succeeded(new(reference, verified));
    }
    private static OperationResult<VerifiedHlsNativeReference> Fail(OperationErrorCode code, string field) =>
        OperationResult<VerifiedHlsNativeReference>.Failed(new(code, OperationErrorCategory.Execution,
            "HLS 원본 참조의 native sample 관계를 지원 범위에서 확인하지 못했습니다.", field));
}
