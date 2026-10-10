using ClipMaker.Media.Infrastructure.ProcessExecution;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class ProcessOutputCollectorTests
{
    [Fact]
    public void FullAcceptsExactly32MiBAndInvalidatesCaptureOnNextByte()
    {
        var collector = new ProcessOutputCollector(ProcessOutputMode.Full);
        byte[] block = new byte[8192];
        for (int count = 0; count < ProcessOutputCollector.FullLimit / block.Length; count++)
            Assert.True(collector.Add(block));
        Assert.Equal(ProcessOutputCollector.FullLimit, collector.Snapshot().Bytes.Length);
        Assert.False(collector.Add(new byte[] { 42 }));
        collector.MarkComplete();
        var result = collector.Snapshot();
        Assert.Empty(result.Bytes.ToArray());
        Assert.Equal(ProcessOutputCollector.FullLimit + 1, result.TotalBytes);
        Assert.True(result.LimitExceeded);
        Assert.True(result.IsTruncated);
        Assert.False(result.IsComplete);
        Assert.True(result.ReachedEndOfStream);
    }

    [Fact]
    public void TailPreservesOrderedLastBytesAcrossWrapAndContinuesDraining()
    {
        var collector = new ProcessOutputCollector(ProcessOutputMode.Tail);
        byte[] prefix = Enumerable.Range(0, ProcessOutputCollector.TailLimit - 3).Select(index => (byte)(index % 251)).ToArray();
        byte[] suffix = Enumerable.Range(0, 20).Select(index => (byte)(200 + index)).ToArray();
        Assert.True(collector.Add(prefix));
        Assert.True(collector.Add(suffix));
        collector.MarkComplete();
        var result = collector.Snapshot();
        Assert.Equal(prefix.Concat(suffix).TakeLast(ProcessOutputCollector.TailLimit).ToArray(), result.Bytes.ToArray());
        Assert.Equal(prefix.Length + suffix.Length, result.TotalBytes);
        Assert.True(result.IsTruncated);
        Assert.False(result.LimitExceeded);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void TailHandlesAChunkLargerThanCapacityAndRetainsLaterSuffix()
    {
        var collector = new ProcessOutputCollector(ProcessOutputMode.Tail);
        byte[] bytes = Enumerable.Range(0, ProcessOutputCollector.TailLimit + 31).Select(index => (byte)(index % 251)).ToArray();
        Assert.True(collector.Add(bytes));
        Assert.True(collector.Add(new byte[] { 251, 252, 253 }));
        Assert.Equal(bytes.Concat(new byte[] { 251, 252, 253 }).TakeLast(ProcessOutputCollector.TailLimit).ToArray(),
            collector.Snapshot().Bytes.ToArray());
    }

    [Fact]
    public void DrainCountsRawBytesWithoutRetainingThem()
    {
        var collector = new ProcessOutputCollector(ProcessOutputMode.Drain);
        Assert.True(collector.Add(new byte[] { 0xef, 0xbb, 0xbf, 0xff, 0x0a }));
        collector.MarkComplete();
        var result = collector.Snapshot();
        Assert.Equal(5, result.TotalBytes);
        Assert.Empty(result.Bytes.ToArray());
        Assert.True(result.IsTruncated);
        Assert.False(result.LimitExceeded);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void SnapshotIsNotChangedByLaterWritesAndDoesNotClaimPrematureEof()
    {
        var collector = new ProcessOutputCollector(ProcessOutputMode.Tail);
        collector.Add(new byte[] { 1, 2 });
        var before = collector.Snapshot();
        collector.Add(new byte[] { 3 });
        collector.MarkComplete();
        Assert.Equal(new byte[] { 1, 2 }, before.Bytes.ToArray());
        Assert.False(before.IsComplete);
        Assert.False(before.ReachedEndOfStream);
        Assert.Equal(new byte[] { 1, 2, 3 }, collector.Snapshot().Bytes.ToArray());
    }
}
