using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class MediaProbeContractTests
{
    private static SignedMediaTime Time(int n)
    { SignedMediaTime.TryCreate(n, 1, out var t, out _); return t!; }
    private static PresentationRange Range()
    { PresentationRange.TryCreate(Time(0), Time(1), out var range, out _); return range!; }
    private static MediaProbeRequest Request(MediaProbeDetail detail, PresentationRange? range = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Path.GetFullPath("local.mp4"), new(1, new string('a', 64)), detail, range);
    private static MediaStreamObservation Stream() => new(0, MediaStreamKind.Video, Time(1), null, null, null, null,
        new("h264", null, null, null, null, null, null));

    [Fact]
    public void DetailedInspectionRequiresRangeLocalIdentityAndCapturedBytes()
    {
        Assert.Throws<ArgumentException>(() => Request(MediaProbeDetail.FramesAndPackets));
        Assert.Throws<ArgumentException>(() => new MediaProbeRequest(Guid.Empty, Guid.NewGuid(), Path.GetFullPath("local.mp4"),
            new(1, new string('a', 64)), MediaProbeDetail.Summary));
        Assert.Throws<ArgumentException>(() => new MediaProbeRequest(Guid.NewGuid(), Guid.NewGuid(), "relative.mp4",
            new(1, new string('a', 64)), MediaProbeDetail.Summary));
        Assert.Throws<ArgumentNullException>(() => new MediaProbeRequest(Guid.NewGuid(), Guid.NewGuid(), Path.GetFullPath("local.mp4"),
            null!, MediaProbeDetail.Summary));
        Assert.Equal(Range(), Request(MediaProbeDetail.FramesAndPackets, Range()).Range);
    }

    [Fact]
    public void DetailedResultRequiresBothSetsAndSnapshotsCopiedCollections()
    {
        var streams = new List<MediaStreamObservation> { Stream() };
        var frames = new List<MediaFrameObservation> { new(0, null, 1, null, null, null, null) };
        var observed = new MediaProbeObservations(streams, frames, [], true, false);
        streams.Clear(); frames.Clear();
        Assert.Single(observed.Streams); Assert.Null(Assert.Single(observed.Frames).Pts);
        Assert.Throws<ArgumentException>(() => new MediaProbeResult(Request(MediaProbeDetail.FramesAndPackets, Range()), "8.1", observed));
        var summary = new MediaProbeObservations([Stream()], [], [], false, false);
        Assert.Same(summary, new MediaProbeResult(Request(MediaProbeDetail.Summary), "8.1", summary).Observations);
        Assert.Throws<ArgumentException>(() => new MediaProbeObservations([Stream(), Stream()], [], [], false, false));
        Assert.Throws<ArgumentException>(() => new MediaProbeObservations([Stream()], [new(7, null, null, null, null, null, null)], [], true, false));
    }
}
