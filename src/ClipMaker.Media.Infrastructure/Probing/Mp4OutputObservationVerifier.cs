using System.Numerics;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Infrastructure.Probing;

// Limited FFmpeg MP4 output interpretation, independent of the requested source
// membership/translation. A local consistency result is not final clip success.
public sealed class Mp4OutputObservationVerifier
{
    public MediaOperationResult<Mp4OutputObservation> Verify(MediaProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (probe.Request.Purpose != MediaProbePurpose.Output || probe.Container is not { } container ||
            !probe.Observations.FramesObserved || !probe.Observations.PacketsObserved) return Fail("OutputObservationMissing");
        if (container.NativeSamples.Status != MediaOperationStatus.Succeeded)
            return MediaOperationResult<Mp4OutputObservation>.Failed(container.NativeSamples.Failure!);
        var raw = probe.Observations; var header = container.Initialization; var evidence = container.NativeSamples.Data!;
        if (header.Tracks.Count != raw.Streams.Count || evidence.Configurations.Count != raw.Streams.Count ||
            raw.Streams.Count(s => s.Kind == MediaStreamKind.Video) != 1 || raw.Streams.Count(s => s.Kind == MediaStreamKind.Audio) > 1)
            return Fail("OutputTrackLayout");
        var result = new List<Mp4OutputStreamObservation>();
        foreach (var stream in raw.Streams)
        {
            var track = header.Tracks.SingleOrDefault(t => t.TrackId == stream.TrackId);
            var config = evidence.Configurations.SingleOrDefault(c => c.TrackId == stream.TrackId);
            if (track is null || config is null || stream.TimeBase != Time(1, track.MediaTimescale) ||
                stream.Codec.Name != config.Codec || stream.Codec.ExtradataSize != config.ConfigBytes)
                return Fail("OutputTrackIdentity");
            var samples = evidence.Samples.Where(s => s.TrackId == track.TrackId).ToArray();
            var packets = raw.Packets.Where(p => p.StreamIndex == stream.Index).ToArray();
            var frames = raw.Frames.Where(f => f.StreamIndex == stream.Index).ToArray();
            if (samples.Length == 0 || packets.Length != samples.Length || track.MovieDuration is null || track.MediaDuration is null)
                return Fail("OutputSampleCompleteness");
            BigInteger rawEnd = samples.Max(s => s.PresentationTicks + s.Duration);
            // mdhd records the muxer's coded presentation span, distinct from the
            // sum of DTS durations and the post-edit movie presentation end.
            if (rawEnd - samples[0].PresentationTicks != track.MediaDuration) return Fail("OutputMediaHeaderDuration");
            SignedMediaTime delay = SignedMediaTime.Zero, validEnd;
            long mediaTime = 0;
            if (track.HasEditList)
            {
                var edits = track.Edits;
                if (edits.Count is < 1 or > 2 || edits.Any(e => e.RateInteger != 1 || e.RateFraction != 0 || e.MovieDuration == 0))
                    return Fail("OutputEditsUnsupported", true);
                if (edits.Count == 2)
                {
                    if (edits[0].MediaTime != -1) return Fail("OutputEditsUnsupported", true);
                    delay = Time(edits[0].MovieDuration, header.MovieTimescale);
                }
                var content = edits[^1];
                if (content.MediaTime < 0) return Fail("OutputEditsUnsupported", true);
                mediaTime = content.MediaTime;
                validEnd = delay + Time(content.MovieDuration, header.MovieTimescale);
                if (edits.Aggregate(BigInteger.Zero, (sum, e) => sum + e.MovieDuration) != track.MovieDuration)
                    return Fail("OutputTrackHeaderDuration");
            }
            else validEnd = Time(track.MovieDuration.Value, header.MovieTimescale);
            if (!delay.TryToNativeTicks(stream.TimeBase, NativeTimestampRounding.Exact, out long delayTicks, out _))
                return Fail("OutputMoviePhaseUnsupported", true);
            BigInteger shift = new BigInteger(delayTicks) - mediaTime;
            var mapped = new List<Mp4OutputSample>();
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i]; var packet = packets[i];
                var pts = sample.PresentationTicks + shift; var dts = sample.DecodeTicks + shift;
                if (pts < long.MinValue || pts > long.MaxValue || dts < long.MinValue || dts > long.MaxValue)
                    return Fail("OutputClockUnsupported", true);
                if (packet.Pts != (long)pts || packet.Dts != (long)dts || packet.Duration != sample.Duration ||
                    packet.Position != sample.ByteOffset || packet.Size != sample.Size || packet.HasUninterpretedSideData ||
                    packet.Flags is null || packet.Flags.Contains('C') ||
                    packet.Flags.Contains('D') && !(stream.Kind == MediaStreamKind.Audio && config.Codec == "aac" &&
                        pts + 1024 <= delayTicks) ||
                    packet.Flags.Contains('K') != ((sample.Flags & 0x01010000) == 0)) return Fail("OutputPacketContradicted");
                mapped.Add(new(sample, (long)pts, (long)dts));
            }
            if (!PresentationRange.TryCreate(delay, validEnd, out var valid, out _)) return Fail("OutputEmptySupport");
            long skip = 0, padding = 0;
            if (stream.Kind == MediaStreamKind.Video)
            {
                if (config.Codec != "h264" || packets.Any(p => p.Skip is not null) || frames.Length != samples.Length || frames.Any(f => f.Pts is null))
                    return Fail("OutputVideoTiming");
                var ordered = mapped.OrderBy(s => s.Pts).ToArray(); var decoded = frames.OrderBy(f => f.Pts).ToArray();
                for (int i = 0; i < ordered.Length; i++)
                    if (decoded[i].Pts != ordered[i].Pts || decoded[i].Duration != ordered[i].Native.Duration ||
                        decoded[i].SampleCount is not null) return Fail("OutputVideoTiming");
                var first = Time(ordered[0].Pts, track.MediaTimescale);
                var end = mapped.Select(s => Time((BigInteger)s.Pts + s.Native.Duration, track.MediaTimescale)).Max()!;
                if (first != valid!.Start || end != valid.End || stream.StartPts != ordered[0].Pts) return Fail("OutputVideoPresentationEnd");
            }
            else
            {
                if (config.Codec != "aac" || stream.Codec.Profile != "LC" || config.SampleRate != track.MediaTimescale ||
                    config.SampleRate != stream.Codec.SampleRate || config.Channels != stream.Codec.Channels ||
                    samples.Any(s => s.PresentationTicks != s.DecodeTicks)) return Fail("OutputAudioCodecUnsupported", true);
                BigInteger skipped = BigInteger.Max(BigInteger.Zero, new BigInteger(mediaTime) - samples[0].PresentationTicks);
                if (skipped > uint.MaxValue || skipped >= (BigInteger)samples.Length * 1024)
                    return Fail("OutputAudioPrimingUnsupported", true);
                skip = (long)skipped;
                for (int i = 0; i < packets.Length; i++)
                {
                    var side = packets[i].Skip;
                    if (side is null) { if (i == 0 && skip != 0) return Fail("OutputAudioSkipMissing"); }
                    else if (side.SkipSamples != (i == 0 ? skip : 0) || side.DiscardPadding != 0 || side.SkipReason != 0 || side.DiscardReason != 0)
                        return Fail("OutputAudioSkipContradicted");
                }
                var audible = new List<(Mp4OutputSample Sample, long Trim)>();
                long remainingSkip = skip;
                for (int i = 0; i < mapped.Count; i++)
                {
                    if (remainingSkip > 0 && i > 0 && (BigInteger)mapped[i].Pts - mapped[i - 1].Pts != 1024)
                        return Fail("OutputAudioPrimingDiscontinuous", true);
                    long trim = Math.Min(remainingSkip, 1024); remainingSkip -= trim;
                    if (trim < 1024) audible.Add((mapped[i], trim));
                }
                if (remainingSkip != 0 || frames.Length != audible.Count || frames.Length == 0) return Fail("OutputAudioFrameCompleteness");
                for (int i = 0; i < audible.Count; i++)
                {
                    if (frames[i].Pts != audible[i].Sample.Pts + audible[i].Trim || frames[i].SampleCount != 1024 - audible[i].Trim ||
                        frames[i].Channels != config.Channels || frames[i].SampleFormat != stream.Codec.SampleFormat)
                        return Fail("OutputAudioDecodedSupport");
                }
                var decodedEnd = Time((BigInteger)frames[^1].Pts!.Value + frames[^1].SampleCount!.Value, track.MediaTimescale);
                if (!decodedEnd.TryToNativeTicks(stream.TimeBase, NativeTimestampRounding.Exact, out long decodedTicks, out _) ||
                    !valid!.End.TryToNativeTicks(stream.TimeBase, NativeTimestampRounding.Exact, out long validTicks, out _) ||
                    decodedTicks < validTicks || decodedTicks - validTicks >= 1024 ||
                    Time(frames[0].Pts!.Value, track.MediaTimescale) != valid.Start || stream.StartPts != frames[0].Pts)
                    return Fail("OutputAudioPresentationEnd");
                padding = decodedTicks - validTicks;
                if (Time(rawEnd + shift, track.MediaTimescale) != valid.End) return Fail("OutputAudioNativeEnd");
            }
            result.Add(new(stream, config, valid!, mapped, skip, padding));
        }
        if (header.MovieDuration is null || header.Tracks.Max(t => t.MovieDuration) != header.MovieDuration)
            return Fail("OutputMovieHeaderDuration");
        return MediaOperationResult<Mp4OutputObservation>.Succeeded(new(probe, result));
    }
    private static SignedMediaTime Time(BigInteger n, BigInteger d)
    { SignedMediaTime.TryCreate(n, d, out var value, out _); return value!; }
    private static MediaOperationResult<Mp4OutputObservation> Fail(string field, bool unsupported = false) =>
        MediaOperationResult<Mp4OutputObservation>.Failed(new(unsupported ? MediaFailureKind.UnsupportedInput : MediaFailureKind.InvalidMedia,
            "MP4 출력의 native sample·edit·decode·표시 끝 관계를 확인하지 못했습니다.", field));
}
