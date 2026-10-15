using System.Numerics;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class PresentationRangeTests
{
    private static SignedMediaTime Time(BigInteger n, BigInteger d)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }

    [Fact]
    public void UsesSignedHalfOpenMembershipAcrossDifferentClocks()
    {
        Assert.True(PresentationRange.TryCreate(Time(-1, 2), Time(1, 3), out var range, out var error));
        Assert.Null(error);
        Assert.True(range.Contains(Time(-24000, 48000)));
        Assert.True(range.Contains(SignedMediaTime.Zero));
        Assert.True(range.Contains(Time(15999, 48000)));
        Assert.False(range.Contains(Time(16000, 48000)));
        Assert.False(range.Contains(Time(-24001, 48000)));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void RejectsEmptyAndReversedIntervals(long start, long end)
    {
        Assert.False(PresentationRange.TryCreate(Time(start, 1), Time(end, 1), out var range, out var error));
        Assert.Null(range);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("End", error.Field);
    }

    [Fact]
    public void PreservesTinyIntervalsBeyondDoublePrecision()
    {
        BigInteger large = BigInteger.Pow(2, 100);
        var start = Time(large, 1);
        var end = start + Time(1, 90000);
        Assert.True(PresentationRange.TryCreate(start, end, out var range, out _));
        Assert.True(range.Contains(start));
        Assert.False(range.Contains(end));
    }

    [Fact]
    public void HasNormalizedValueEqualityAndGuardsNulls()
    {
        Assert.True(PresentationRange.TryCreate(Time(-1, 2), Time(1, 3), out var first, out _));
        Assert.True(PresentationRange.TryCreate(Time(-2, 4), Time(2, 6), out var second, out _));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Throws<ArgumentNullException>(() => PresentationRange.TryCreate(null!, SignedMediaTime.Zero, out _, out _));
        Assert.Throws<ArgumentNullException>(() => PresentationRange.TryCreate(SignedMediaTime.Zero, null!, out _, out _));
        Assert.Throws<ArgumentNullException>(() => first.Contains(null!));
    }
}
