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
            bool audio = stream.Kind == MediaStreamKind.Audio;
            long trailingPadding = 0;
            if (audio && samples[^1].Duration is > 0 and < 1024)
            {
                var terminal = samples[^1];
                SignedMediaTime.TryCreate((BigInteger)terminal.PresentationTicks + terminal.Duration + translation,
                    track.MediaTimescale, out var terminalEnd, out _);
                if (movieEnd is null || terminalEnd != movieEnd ||
                    samples.Any(s => s.PresentationTicks != s.DecodeTicks) || samples[..^1].Any(s => s.Duration != 1024 ||
                        (BigInteger)s.PresentationTicks + s.Duration > terminal.PresentationTicks))
                    return Fail(MediaFailureKind.UnsupportedInput, "NativeTerminalDurationUnsupported");
                trailingPadding = 1024 - terminal.Duration;
            }
            BigInteger skipped = audio ? BigInteger.Max(0, -(samples[0].PresentationTicks + translation)) : 0;
            if (skipped > uint.MaxValue || skipped > 0 && (!track.HasEditList || samples[0].PresentationTicks != 0 ||
                samples[0].DecodeTicks != 0 || samples.Any(s => s.PresentationTicks != s.DecodeTicks) ||
                samples.Where((s, i) => i != samples.Length - 1 || trailingPadding == 0).Any(s => s.Duration != 1024)))
                return Fail(MediaFailureKind.UnsupportedInput, "NativeSkipUnsupported");
            long leadingSkip = (long)skipped;
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i]; var packet = packets[i];
                if (sample.PresentationTicks + translation < long.MinValue || sample.PresentationTicks + translation > long.MaxValue ||
                    sample.DecodeTicks + translation < long.MinValue || sample.DecodeTicks + translation > long.MaxValue)
                    return Fail(MediaFailureKind.UnsupportedInput, "NativeClockOverflow");
                if (packet.Pts != (long)(sample.PresentationTicks + translation) || packet.Dts != (long)(sample.DecodeTicks + translation) ||
                    packet.Duration != sample.Duration || packet.Size != sample.Size || packet.Position != sample.ByteOffset)
                    return Fail(MediaFailureKind.InvalidMedia, "NativePacketTiming");
                bool discardedPrefix = audio && leadingSkip > 0 && sample.PresentationTicks + sample.Duration + translation <= 0;
                if (packet.HasUninterpretedSideData || packet.Flags is null || packet.Flags.Contains('C') || packet.Flags.Contains('D') && !discardedPrefix)
                    return Fail(MediaFailureKind.InvalidMedia, "NativePacketState");
                if (!MatchesPacketSync(stream.Kind, sample.Flags, packet))
                    return Fail(MediaFailureKind.InvalidMedia, "NativeSyncState");
                if (packet.Skip is { } skip)
                {
                    if (skip.SkipSamples is null || skip.DiscardPadding is null || skip.SkipReason is null || skip.DiscardReason is null)
                        return Fail(MediaFailureKind.InvalidMedia, "NativeSkipMissing");
                    if (skip.DiscardPadding != 0 || skip.SkipReason != 0 || skip.DiscardReason != 0 || !audio && skip.SkipSamples != 0)
                        return Fail(MediaFailureKind.UnsupportedInput, "NativeSkipUnsupported");
                    if (skip.SkipSamples != (i == 0 ? leadingSkip : 0))
                        return Fail(MediaFailureKind.InvalidMedia, "NativeSkipContradicted");
                }
                else if (i == 0 && leadingSkip != 0) return Fail(MediaFailureKind.InvalidMedia, "NativeSkipMissing");
            }
            // A source AAC edit may consume entire leading packets and part of the next.
            // Keep coded packet identity above; independently predict every effective decoded frame.
            var expected = new List<(BigInteger Pts, long Duration)>();
            long remainingSkip = leadingSkip;
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                if (remainingSkip > 0 && i > 0 && sample.PresentationTicks != samples[i - 1].PresentationTicks + samples[i - 1].Duration)
                    return Fail(MediaFailureKind.UnsupportedInput, "NativeSkipDiscontinuous");
                long trim = Math.Min(remainingSkip, sample.Duration);
                remainingSkip -= trim;
                if (trim < sample.Duration) expected.Add((sample.PresentationTicks + translation + trim, sample.Duration - trim));
            }
            if (remainingSkip != 0 || expected.Count == 0 || expected.Count != frames.Length)
                return Fail(MediaFailureKind.InvalidMedia, "NativeSampleCompleteness");
            var orderedExpected = expected.OrderBy(s => s.Pts).ToArray();
            if (frames.Any(f => f.Pts is null)) return Fail(MediaFailureKind.InvalidMedia, "NativeFramePtsMissing");
            var orderedFrames = frames.OrderBy(f => f.Pts).ToArray();
            for (int i = 0; i < orderedExpected.Length; i++)
            {
                var effective = orderedExpected[i]; var frame = orderedFrames[i];
                if (frame.Pts != effective.Pts || frame.Duration != effective.Duration)
                    return Fail(MediaFailureKind.InvalidMedia, "NativeFrameTiming");
                long decodedSamples = effective.Duration + (i == orderedExpected.Length - 1 ? trailingPadding : 0);
                if (audio && (frame.SampleCount != decodedSamples ||
                    frame.SampleRate is { } rate && rate != config.SampleRate || frame.Channels is { } channels && channels != config.Channels))
                    return Fail(MediaFailureKind.InvalidMedia, "NativeAudioSamples");
            }
            if (stream.StartPts != orderedExpected[0].Pts)
                return Fail(MediaFailureKind.InvalidMedia, "NativeStartTiming");
            SignedMediaTime.TryCreate(orderedExpected[0].Pts * stream.TimeBase.Numerator,
                stream.TimeBase.Denominator, out var start, out _);
            BigInteger last = orderedExpected.Max(s => s.Pts + s.Duration);
            SignedMediaTime.TryCreate(last * stream.TimeBase.Numerator, stream.TimeBase.Denominator, out var end, out _);
            PresentationRange.TryCreate(start!, end!, out var range, out _);
            if (range is null) return Fail(MediaFailureKind.InvalidMedia, "NativeEmptyRange");
            if (movieEnd is not null && (range.Start.Numerator < 0 || range.End.CompareTo(movieEnd) > 0))
                return Fail(MediaFailureKind.UnsupportedInput, "MovieEditClippingUnsupported");
            verified.Add(new(stream, config, range, leadingSkip, trailingPadding));
        }
        return MediaOperationResult<Mp4NativeObservation>.Succeeded(new(verified));
    }
    private static MediaOperationResult<Mp4NativeObservation> Fail(MediaFailureKind code, string field) =>
        MediaOperationResult<Mp4NativeObservation>.Failed(new(code,
            "로컬 MP4의 native sample 관계를 지원 범위에서 확인하지 못했습니다.", field));

    internal static bool MatchesPacketSync(MediaStreamKind kind, uint flags, MediaPacketObservation packet)
    {
        bool nativeSync = (flags & 0x01010000) == 0, key = packet.Flags?.Contains('K') == true;
        if (nativeSync == key) return true;
        return kind == MediaStreamKind.Video && !nativeSync && key && (flags & 0x10000) != 0 &&
            packet.RecoveryPoint is { RecoveryFrameCount: 0, ExactMatch: true, BrokenLink: false,
                ChangingSliceGroupIdc: 0, AllSlicesIntra: true } proof && proof.Position == packet.Position &&
            proof.Size == packet.Size && proof.Sha256.Length == 64 && proof.Sha256.All(char.IsAsciiHexDigit);
    }
}
