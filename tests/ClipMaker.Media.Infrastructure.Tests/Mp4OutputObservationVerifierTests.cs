using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class Mp4OutputObservationVerifierTests
{
    [Theory]
    [InlineData("success", null)]
    [InlineData("skip", "OutputAudioSkipContradicted")]
    [InlineData("missing-skip", "OutputAudioSkipMissing")]
    [InlineData("discard-audible", "OutputPacketContradicted")]
    [InlineData("partial-size", "OutputAudioDecodedSupport")]
    [InlineData("partial-pts", "OutputAudioDecodedSupport")]
    [InlineData("missing-frame", "OutputAudioFrameCompleteness")]
    public void MultiPacketPrimingRequiresExactNativeEditSkipAndAudibleFrames(string fault, string? field)
    {
        var video = new MediaStreamObservation(0, MediaStreamKind.Video, Time(1, 44100), 0, null, null, 1, new("h264", "High", "yuv420p", null, null, null, 1));
        var audio = new MediaStreamObservation(1, MediaStreamKind.Audio, Time(1, 44100), 0, null, null, 2, new("aac", "LC", null, "fltp", 44100, 2, 2));
        var frames = new List<MediaFrameObservation> { new(0, 0, 2496, null, null, null, null),
            new(1, fault == "partial-pts" ? 1 : 0, 448, fault == "partial-size" ? 447 : 448, "fltp", 44100, 2),
            new(1, 448, 1024, 1024, "fltp", 44100, 2), new(1, 1472, 1024, 1024, "fltp", 44100, 2) };
        if (fault == "missing-frame") frames.RemoveAt(1);
        var samples = new List<Mp4NativeSample> { new(1, 0, 0, 2496, 4, 0, 100) };
        var packets = new List<MediaPacketObservation> { new(0, 0, 0, 2496, "K_", null, false, 4, 100) };
        for (int i = 0; i < 4; i++)
        {
            samples.Add(new(2, i * 1024, i * 1024, 1024, 4, 0, 104 + i * 4));
            packets.Add(new(1, i * 1024 - 1600, i * 1024 - 1600, 1024, i == 0 || fault == "discard-audible" && i == 1 ? "KD_" : "K_",
                i == 0 && fault != "missing-skip" ? new(fault == "skip" ? 1599u : 1600u, 0, 0, 0) : null, false, 4, 104 + i * 4));
        }
        var native = new Mp4NativeSampleEvidence([new(1, "h264", 1, new string('a', 64), null, null), new(2, "aac", 2, new string('b', 64), 44100, 2)], samples);
        var header = new Mp4TimingObservation(44100, [new(1, 44100, false, [], 2496, 2496), new(2, 44100, true, [new(2496, 1600, 1, 0)], 2496, 4096)], 2496);
        PresentationRange.TryCreate(Time(0), Time(1), out var range, out _);
        var request = new MediaProbeRequest(Guid.NewGuid(), Guid.NewGuid(), Path.GetFullPath("output.mp4"), new(200, new string('c', 64)), MediaProbeDetail.FramesAndPackets, range, true, MediaProbePurpose.Output);
        var result = new Mp4OutputObservationVerifier().Verify(new(request, "fixture", new([video, audio], frames, packets, true, true), new(header, [], MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(native))));
        if (field is null)
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status); var actual = result.Data!.Streams[1];
            Assert.Equal(1600, actual.PrimingSkippedSamples); Assert.Equal(0, actual.DecodedTailPaddingSamples);
            Assert.Equal(Time(0), actual.ValidPresentation.Start); Assert.Equal(Time(2496, 44100), actual.ValidPresentation.End);
        }
        else { Assert.Equal(field, result.Failure!.Field); Assert.Null(result.Data); }
    }
    private static SignedMediaTime Time(long n, long d = 1)
    { SignedMediaTime.TryCreate(n, d, out var value, out _); return value!; }
    private static MediaProbeResult Probe(string fault)
    {
        var video = new MediaStreamObservation(0, MediaStreamKind.Video, Time(1, 6000), 0, null, null, 1,
            new("h264", "High", "yuv420p", null, null, null, 1));
        var audio = new MediaStreamObservation(1, MediaStreamKind.Audio, Time(1, 48000), 0, null, null, 2,
            new("aac", "LC", null, "fltp", 48000, 2, 2));
        var frames = new MediaFrameObservation[] { new(0, fault == "frame-pts" ? 1 : 0, 100, null, null, null, null),
            new(0, 100, 100, null, null, null, null), new(1, 0, 1024, fault == "padding" ? 1023 : 1024, "fltp", 48000, 2) };
        var packets = new MediaPacketObservation[] { new(0, 0, -1000, 100, "K_", null, false, 4, 100),
            new(0, 100, -900, 100, "__", null, false, 4, 104),
            new(1, -1024, -1024, 1024, fault == "discard-priming" ? "KD_" : "K_", new(fault == "skip" ? 1023u : 1024u, 0, 0, 0), false, 4, 108),
            new(1, 0, 0, 256, fault == "discard-audible" ? "KD_" : "K_", null, false, 4, 112) };
        var native = new Mp4NativeSampleEvidence([new(1, "h264", 1, new string('a', 64), null, null), new(2, "aac", 2, new string('b', 64), 48000, 2)],
            [new(1, 0, 1000, 100, 4, 0, 100), new(1, 100, 1100, 100, 4, 0x01010000, 104),
             new(2, 0, 0, 1024, 4, 0, 108), new(2, 1024, 1024, 256, 4, 0, 112)]);
        var edits = fault == "phase" ? new Mp4EditObservation[] { new(1, -1, 1, 0), new(1600, 1000, 1, 0) } : [new(1600, 1000, 1, 0)];
        var header = new Mp4TimingObservation(48000, [new(1, 6000, true, edits, fault is "edit-duration" or "phase" ? 1601u : 1600u,
            fault == "media-duration" ? 201u : 200u), new(2, 48000, true, [new(256, 1024, 1, 0)], 256, 1280)], fault is "movie" or "phase" ? 1601u : 1600u);
        var container = new Mp4FileTimingEvidence(header, [], MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(native));
        PresentationRange.TryCreate(Time(0), Time(1), out var range, out _);
        var request = new MediaProbeRequest(Guid.NewGuid(), Guid.NewGuid(), Path.GetFullPath("output.mp4"), new(200, new string('c', 64)),
            MediaProbeDetail.FramesAndPackets, range, true, MediaProbePurpose.Output);
        return new(request, "fixture", new([video, audio], frames, packets, true, true), container);
    }

    [Theory]
    [InlineData("success", null)]
    [InlineData("discard-priming", null)]
    [InlineData("discard-audible", "OutputPacketContradicted")]
    [InlineData("media-duration", "OutputMediaHeaderDuration")]
    [InlineData("edit-duration", "OutputTrackHeaderDuration")]
    [InlineData("frame-pts", "OutputVideoTiming")]
    [InlineData("skip", "OutputAudioSkipContradicted")]
    [InlineData("padding", "OutputAudioDecodedSupport")]
    [InlineData("movie", "OutputMovieHeaderDuration")]
    [InlineData("phase", "OutputMoviePhaseUnsupported")]
    public void NativeEditHeaderAndDecodedSupportAreIndependentChecks(string fault, string? field)
    {
        var result = new Mp4OutputObservationVerifier().Verify(Probe(fault));
        if (field is null)
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
            Assert.Equal(Time(1, 30), result.Data!.Streams[0].ValidPresentation.End);
            Assert.Equal(1024, result.Data.Streams[1].PrimingSkippedSamples);
            Assert.Equal(768, result.Data.Streams[1].DecodedTailPaddingSamples);
            Assert.Equal(Time(256, 48000), result.Data.Streams[1].ValidPresentation.End);
        }
        else { Assert.Null(result.Data); Assert.Equal(field, result.Failure!.Field); }
    }
}
