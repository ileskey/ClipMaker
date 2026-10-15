using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class FrameRateTests
{
    [Theory]
    [InlineData(60, 2, 30, 1)]
    [InlineData(60000, 2002, 30000, 1001)]
    [InlineData(2997, 100, 2997, 100)]
    [InlineData(long.MaxValue, long.MaxValue, 1, 1)]
    [InlineData(long.MaxValue, 1, long.MaxValue, 1)]
    public void ReducesPositiveRationalWithoutStandardRateGuessing(
        long numerator, long denominator, long expectedNumerator, long expectedDenominator)
    {
        Assert.True(FrameRate.TryCreate(numerator, denominator, out var rate, out var error));
        Assert.Null(error);
        Assert.Equal(expectedNumerator, rate.Numerator);
        Assert.Equal(expectedDenominator, rate.Denominator);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(long.MinValue, long.MinValue)]
    public void RejectsNonPositiveComponents(long numerator, long denominator)
    {
        Assert.False(FrameRate.TryCreate(numerator, denominator, out var rate, out var error));
        Assert.Null(rate);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("FrameRate", error.Field);
    }

    [Fact]
    public void NormalizedRatesHaveValueEquality()
    {
        Assert.True(FrameRate.TryCreate(60, 2, out var first, out _));
        Assert.True(FrameRate.TryCreate(30, 1, out var second, out _));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("30/1", first.ToString());
    }
}
