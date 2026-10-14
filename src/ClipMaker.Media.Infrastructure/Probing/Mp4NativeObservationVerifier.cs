using System.Numerics;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Infrastructure.Probing;

public sealed class Mp4NativeObservationVerifier
{
    public MediaOperationResult<Mp4NativeObservation> Verify(MediaProbeObservations observations, Mp4FileTimingEvidence container)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(container);
        if (container.NativeSamples is null) return Fail(MediaFailureKind.InvalidMedia, "NativeSamplesMissing");
        if (container.NativeSamples.Status != MediaOperationStatus.Succeeded)
            return MediaOperationResult<Mp4NativeObservation>.Failed(container.NativeSamples.Failure!);
        if (!observations.PacketsObserved || !observations.FramesObserved)
            return Fail(MediaFailureKind.UnsupportedInput, "NativeEditsUnsupported");
        var evidence = container.NativeSamples.Data!;
        if (container.Initialization.Tracks.Count != observations.Streams.Count ||
            container.Initialization.Tracks.Select(t => t.TrackId).Distinct().Count() != container.Initialization.Tracks.Count)
            return Fail(MediaFailureKind.InvalidMedia, "NativeTrackIdentity");
        if (observations.Streams.Count != evidence.Configurations.Count ||
            observations.Streams.Count(s => s.Kind == MediaStreamKind.Video) > 1 ||
            observations.Streams.Count(s => s.Kind == MediaStreamKind.Audio) > 1)
            return Fail(MediaFailureKind.UnsupportedInput, "NativeTrackLayout");
        var verified = new List<Mp4NativeStreamObservation>();
        foreach (var stream in observations.Streams)
        {
            var track = container.Initialization.Tracks.SingleOrDefault(track => track.TrackId == stream.TrackId);
            if (track is null || stream.TimeBase.Numerator != 1 || stream.TimeBase.Denominator != track.MediaTimescale)
                return Fail(MediaFailureKind.InvalidMedia, "NativeTrackClock");
            BigInteger translation = 0;
            SignedMediaTime? movieEnd = null;
            if (track.HasEditList)
            {
                if (track.Edits.Count != 1 || track.Edits[0] is not { MediaTime: >= 0, MovieDuration: > 0, RateInteger: 1, RateFraction: 0 } edit)
                    return Fail(MediaFailureKind.UnsupportedInput, "NativeEditsUnsupported");
                translation = -new BigInteger(edit.MediaTime);
                SignedMediaTime.TryCreate(edit.MovieDuration, container.Initialization.MovieTimescale, out movieEnd, out _);
                if (movieEnd is null) return Fail(MediaFailureKind.InvalidMedia, "MovieClock");
            }
            var config = evidence.Configurations.SingleOrDefault(c => c.TrackId == stream.TrackId);
            if (config is null || config.Codec != stream.Codec.Name || config.ConfigBytes != stream.Codec.ExtradataSize)
                return Fail(MediaFailureKind.InvalidMedia, "NativeCodecIdentity");
            if (stream.Kind == MediaStreamKind.Video && config.Codec != "h264" || stream.Kind == MediaStreamKind.Audio &&
                (config.Codec != "aac" || stream.Codec.Profile != "LC" || stream.Codec.SampleRate != config.SampleRate ||
                    stream.Codec.Channels != config.Channels || stream.TimeBase.Denominator != config.SampleRate))
                return Fail(MediaFailureKind.UnsupportedInput, "NativeCodecParameters");
            var samples = evidence.Samples.Where(s => s.TrackId == stream.TrackId).ToArray();
            var packets = observations.Packets.Where(p => p.StreamIndex == stream.Index).ToArray();
            var frames = observations.Frames.Where(f => f.StreamIndex == stream.Index).ToArray();
            if (samples.Length == 0 || samples.Length != packets.Length)
                return Fail(MediaFailureKind.InvalidMedia, "NativeSampleCompleteness");
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i]; var packet = packets[i];
                if (sample.PresentationTicks + translation < long.MinValue || sample.PresentationTicks + translation > long.MaxValue ||
                    sample.DecodeTicks + translation < long.MinValue || sample.DecodeTicks + translation > long.MaxValue)
                    return Fail(MediaFailureKind.UnsupportedInput, "NativeClockOverflow");
                if (packet.Pts != (long)(sample.PresentationTicks + translation) || packet.Dts != (long)(sample.DecodeTicks + translation) ||
                    packet.Duration != sample.Duration || packet.Size != sample.Size || packet.Position != sample.ByteOffset)
                    return Fail(MediaFailureKind.InvalidMedia, "NativePacketTiming");
                if (packet.HasUninterpretedSideData || packet.Flags is null || packet.Flags.Contains('C') || packet.Flags.Contains('D'))
                    return Fail(MediaFailureKind.InvalidMedia, "NativePacketState");
                bool nativeSync = (sample.Flags & 0x01010000) == 0;
                if (nativeSync != packet.Flags.Contains('K'))
                    return Fail(MediaFailureKind.InvalidMedia, "NativeSyncState");
                if (packet.Skip is { } skip)
                {
                    if (skip.SkipSamples is null || skip.DiscardPadding is null || skip.SkipReason is null || skip.DiscardReason is null)
                        return Fail(MediaFailureKind.InvalidMedia, "NativeSkipMissing");
                    if (skip.SkipSamples != 0 || skip.DiscardPadding != 0 || skip.SkipReason != 0 || skip.DiscardReason != 0)
                        return Fail(MediaFailureKind.UnsupportedInput, "NativeSkipUnsupported");
                }
            }
            // Skip/padding can consume a complete packet or trim a decoded frame.
            // Identify the unsupported transformation before treating its frame count as corruption.
            if (samples.Length != frames.Length)
                return Fail(MediaFailureKind.InvalidMedia, "NativeSampleCompleteness");
            var orderedSamples = samples.OrderBy(s => s.PresentationTicks).ToArray();
            if (frames.Any(f => f.Pts is null)) return Fail(MediaFailureKind.InvalidMedia, "NativeFramePtsMissing");
            var orderedFrames = frames.OrderBy(f => f.Pts).ToArray();
            for (int i = 0; i < orderedSamples.Length; i++)
            {
                var sample = orderedSamples[i]; var frame = orderedFrames[i];
                if (frame.Pts != (long)(sample.PresentationTicks + translation) || frame.Duration != sample.Duration)
                    return Fail(MediaFailureKind.InvalidMedia, "NativeFrameTiming");
                if (stream.Kind == MediaStreamKind.Audio && (frame.SampleCount != sample.Duration ||
                    frame.SampleRate is { } rate && rate != config.SampleRate || frame.Channels is { } channels && channels != config.Channels))
                    return Fail(MediaFailureKind.InvalidMedia, "NativeAudioSamples");
            }
            if (stream.StartPts != (long)(orderedSamples[0].PresentationTicks + translation))
                return Fail(MediaFailureKind.InvalidMedia, "NativeStartTiming");
            SignedMediaTime.TryCreate((orderedSamples[0].PresentationTicks + translation) * stream.TimeBase.Numerator,
                stream.TimeBase.Denominator, out var start, out _);
            BigInteger last = orderedSamples.Max(s => s.PresentationTicks + s.Duration) + translation;
            SignedMediaTime.TryCreate(last * stream.TimeBase.Numerator, stream.TimeBase.Denominator, out var end, out _);
            PresentationRange.TryCreate(start!, end!, out var range, out _);
            if (range is null) return Fail(MediaFailureKind.InvalidMedia, "NativeEmptyRange");
            if (movieEnd is not null && (range.Start.Numerator < 0 || range.End.CompareTo(movieEnd) > 0))
                return Fail(MediaFailureKind.UnsupportedInput, "MovieEditClippingUnsupported");
            verified.Add(new(stream, config, range));
        }
        return MediaOperationResult<Mp4NativeObservation>.Succeeded(new(verified));
    }
    private static MediaOperationResult<Mp4NativeObservation> Fail(MediaFailureKind code, string field) =>
        MediaOperationResult<Mp4NativeObservation>.Failed(new(code,
            "로컬 MP4의 native sample 관계를 지원 범위에서 확인하지 못했습니다.", field));
}
