using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class Mp4NativeObservationVerifierTests
{
    [Theory]
    [InlineData("skip", null, MediaFailureKind.UnsupportedInput)]
    [InlineData("missing-skip", "NativeSkipMissing", MediaFailureKind.InvalidMedia)]
    [InlineData("no-skip", "NativeSampleCompleteness", MediaFailureKind.InvalidMedia)]
    [InlineData("missing-packet", "NativeSampleCompleteness", MediaFailureKind.InvalidMedia)]
    [InlineData("wrong-skip", "NativeSkipContradicted", MediaFailureKind.InvalidMedia)]
    [InlineData("tail-padding", "NativeSkipUnsupported", MediaFailureKind.UnsupportedInput)]
    [InlineData("wrong-frame-pts", "NativeFrameTiming", MediaFailureKind.InvalidMedia)]
    [InlineData("wrong-frame-size", "NativeFrameTiming", MediaFailureKind.InvalidMedia)]
    [InlineData("missing-declared-skip", "NativeSkipMissing", MediaFailureKind.InvalidMedia)]
    public void AudioSkipAcrossPacketBoundaryRequiresExactEditPacketAndEffectiveFrameCorrespondence(string fault, string? field, MediaFailureKind kind)
    {
        SignedMediaTime.TryCreate(1, 44100, out var clock, out _);
        long shift = fault == "no-skip" ? 0 : -1600;
        var stream = new MediaStreamObservation(0, MediaStreamKind.Audio, clock!, 0, null, null, 1,
            new("aac", "LC", null, "fltp", 44100, 2, 2));
        var samples = new Mp4NativeSampleEvidence([new(1, "aac", 2, new string('a', 64), 44100, 2)],
            [new(1, 0, 0, 1024, 10, 0, 100), new(1, 1024, 1024, 1024, 20, 0, 110)]);
        var skip = fault is "no-skip" or "missing-declared-skip" ? null : new MediaPacketSkipObservation(
            fault == "missing-skip" ? null : fault == "wrong-skip" ? 1599u : 1600u, fault == "tail-padding" ? 1u : 0u, 0, 0);
        var packets = new[] { new MediaPacketObservation(0, shift, shift, 1024, "K__", skip, false, 10, 100),
            new MediaPacketObservation(0, 1024 + shift, 1024 + shift, 1024, "K__", null, false, 20, 110) };
        int count = fault == "wrong-frame-size" ? 447 : 448;
        var raw = new MediaProbeObservations([stream], [new(0, fault == "wrong-frame-pts" ? 1 : 0, count, count, "fltp", 44100, 2)],
            fault == "missing-packet" ? packets[..1] : packets, true, true);
        var container = new Mp4FileTimingEvidence(new(44100, [new(1, 44100, shift != 0, shift == 0 ? [] : [new(44100, 1600, 1, 0)])]), [],
            MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(samples));
        var result = new Mp4NativeObservationVerifier().Verify(raw, container);
        if (field is null)
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
            var actual = Assert.Single(result.Data!.Streams); Assert.Equal(1600, actual.LeadingSkippedSamples);
            Assert.Equal(SignedMediaTime.Zero, actual.ObservedRange.Start);
            SignedMediaTime.TryCreate(448, 44100, out var end, out _); Assert.Equal(end, actual.ObservedRange.End);
            return;
        }
        Assert.Equal(MediaOperationStatus.Failed, result.Status); Assert.Null(result.Data);
        Assert.Equal(field, result.Failure!.Field); Assert.Equal(kind, result.Failure.Kind);
    }
    [Theory]
    [InlineData("success", null)]
    [InlineData("edit", null)]
    [InlineData("missing-pts", "NativeFramePtsMissing")]
    [InlineData("packet", "NativePacketTiming")]
    [InlineData("clock", "NativeTrackClock")]
    [InlineData("skip", "NativeSkipUnsupported")]
    [InlineData("multiple-edits", "NativeEditsUnsupported")]
    [InlineData("partial", "NativeEditsUnsupported")]
    public void LimitedNativeGateChecksCorrespondenceWithoutInferringPreparationOrSourceCoverage(string fault, string? field)
    {
        SignedMediaTime.TryCreate(1, 6000, out var clock, out _);
        SignedMediaTime.TryCreate(1, 48000, out var badClock, out _);
        bool edit = fault is "edit" or "multiple-edits";
        long pts = edit ? 900 : 1000;
        var stream = new MediaStreamObservation(0, MediaStreamKind.Video, fault == "clock" ? badClock! : clock!, pts,
            null, null, 1, new("h264", null, "yuv420p", null, null, null, 1));
        var frames = new[] { new MediaFrameObservation(0, fault == "missing-pts" ? null : pts, 100, null, null, null, null) };
        var packet = new MediaPacketObservation(0, fault == "packet" ? pts + 1 : pts, pts - 100, 100, "K_",
            fault == "skip" ? new(1, 0, 0, 0) : null, false, 10, 20);
        var raw = new MediaProbeObservations([stream], frames, fault == "partial" ? [] : [packet], true, fault != "partial");
        var edits = edit ? new[] { new Mp4EditObservation(2000, 100, 1, 0) } : [];
        if (fault == "multiple-edits") edits = [edits[0], edits[0]];
        var samples = new Mp4NativeSampleEvidence([new(1, "h264", 1, new string('a', 64), null, null)],
            [new(1, 900, 1000, 100, 10, 0, 20)]);
        var container = new Mp4FileTimingEvidence(new(1000, [new(1, 6000, edit, edits)]), [],
            MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(samples));
        var result = new Mp4NativeObservationVerifier().Verify(raw, container);
        if (field is null)
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
            var actual = Assert.Single(result.Data!.Streams);
            SignedMediaTime.TryCreate(pts, 6000, out var start, out _);
            Assert.Equal(start, actual.ObservedRange.Start);
            Assert.Same(stream, actual.Stream);
            Assert.Equal(MediaTimeBasis.Demux, raw.TimeBasis);
        }
        else
        {
            Assert.Equal(MediaOperationStatus.Failed, result.Status);
            Assert.Equal(field, result.Failure!.Field);
            Assert.Null(result.Data);
        }
    }
}
