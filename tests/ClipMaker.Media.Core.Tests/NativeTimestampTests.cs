using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class NativeTimestampTests
{
    private static SignedMediaTime Time(long n, long d)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }

    [Theory]
    [InlineData(-312)]
    [InlineData(0)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void RetainsObservedSignedPtsAndRoundTripsNativeClock(long pts)
    {
        var clock = Time(1, 48000);
        Assert.True(NativeTimestamp.TryCreate(pts, clock, out var timestamp, out var error));
        Assert.Null(error);
        Assert.Equal(pts, timestamp.Pts);
        Assert.Equal(clock, timestamp.TimeBase);
        Assert.True(timestamp.PresentationTime.TryToNativeTicks(clock, NativeTimestampRounding.Exact, out var actual, out _));
        Assert.Equal(pts, actual);
    }

    [Fact]
    public void ScalesBothTimeBaseComponentsAndPreservesObservationIdentity()
    {
        Assert.True(NativeTimestamp.TryCreate(-3, Time(2, 5), out var first, out _));
        Assert.True(NativeTimestamp.TryCreate(-6, Time(1, 5), out var second, out _));
        Assert.Equal(Time(-6, 5), first.PresentationTime);
        Assert.Equal(first.PresentationTime, second.PresentationTime);
        Assert.NotEqual(first, second);
        Assert.True(NativeTimestamp.TryCreate(-3, Time(4, 10), out var equal, out _));
        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveTimeBase(long n)
    {
        Assert.False(NativeTimestamp.TryCreate(1, Time(n, 48000), out var timestamp, out var error));
        Assert.Null(timestamp);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("TimeBase", error.Field);
    }

    [Fact]
    public void RejectsNullTimeBase() =>
        Assert.Throws<ArgumentNullException>(() => NativeTimestamp.TryCreate(0, null!, out _, out _));
}
