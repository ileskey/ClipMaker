using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class MediaTimeRangeTests
{
    private static Timecode Time(string text, long numerator = 30, long denominator = 1)
    {
        Assert.True(FrameRate.TryCreate(numerator, denominator, out var rate, out _));
        Assert.True(TimecodeInput.TryParse(text, out var input, out _));
        Assert.True(Timecode.TryCreate(input, rate, out var time, out _));
        return time;
    }

    private static MediaTimeRange Range(string start, string end, long numerator = 30, long denominator = 1)
    {
        Assert.True(MediaTimeRange.TryCreate(Time(start, numerator, denominator),
            Time(end, numerator, denominator), out var range, out var error));
        Assert.Null(error);
        return range;
    }

    [Theory]
    [InlineData("00:00:00:00", "00:00:00:01", 30, 1)]
    [InlineData("00:00:10:15", "00:00:20:00", 2997, 100)]
    [InlineData("00:00:10:15", "00:00:20:00", 30000, 1001)]
    [InlineData("100:00:00:00", "100:00:00:100", 120, 1)]
    public void CreatesForwardRangeAndRetainsValidatedEndpoints(
        string startText, string endText, long numerator, long denominator)
    {
        var start = Time(startText, numerator, denominator);
        var end = Time(endText, numerator, denominator);
        Assert.True(MediaTimeRange.TryCreate(start, end, out var range, out var error));
        Assert.Null(error);
        Assert.Same(start, range.Start);
        Assert.Same(end, range.End);
        Assert.Equal(start.FrameRate, range.FrameRate);
    }

    [Theory]
    [InlineData("00:00:00:00", "00:00:00:00")]
    [InlineData("00:00:10:15", "00:00:10:15")]
    [InlineData("00:00:10:15", "00:00:10:14")]
    [InlineData("100:00:00:00", "99:59:59:29")]
    public void RejectsEmptyOrReversedRangesWithoutChangingInput(string startText, string endText)
    {
        var start = Time(startText);
        var end = Time(endText);
        Assert.False(MediaTimeRange.TryCreate(start, end, out var range, out var error));
        Assert.Null(range);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("End", error.Field);
        Assert.Equal(startText, start.ToString());
        Assert.Equal(endText, end.ToString());
    }

    [Theory]
    [InlineData(25, 1, 30, 1)]
    [InlineData(2997, 100, 30000, 1001)]
    [InlineData(30, 1, 60, 1)]
    public void RejectsDifferentRatesEvenWhenEndpointsCanBeCompared(
        long startNumerator, long startDenominator, long endNumerator, long endDenominator)
    {
        Assert.False(MediaTimeRange.TryCreate(Time("00:00:00:00", startNumerator, startDenominator),
            Time("00:00:01:00", endNumerator, endDenominator), out var range, out var error));
        Assert.Null(range);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("FrameRate", error.Field);
    }

    [Fact]
    public void AcceptsEquivalentReducedRates()
    {
        Assert.True(MediaTimeRange.TryCreate(Time("00:00:00:00", 60000, 2002),
            Time("00:00:01:00", 30000, 1001), out var range, out var error));
        Assert.Null(error);
        Assert.Equal(30000, range.FrameRate.Numerator);
        Assert.Equal(1001, range.FrameRate.Denominator);
    }

    [Fact]
    public void AcceptsPositiveExactRangeWhoseEndpointsRoundToTheSameTick()
    {
        var range = Range("00:00:00:00", "00:00:00:01", 20000000);
        Assert.True(range.Start.TryToTimeSpan(out var start, out _));
        Assert.True(range.End.TryToTimeSpan(out var end, out _));
        Assert.Equal(start, end);
        Assert.True(range.End.CompareTo(range.Start) > 0);
    }

    [Theory]
    [InlineData(105000000)]
    [InlineData(105000001)]
    [InlineData(long.MaxValue)]
    public void AllowsEndAtOrBeforeKnownDuration(long durationTicks)
    {
        var range = Range("00:00:10:00", "00:00:10:15");
        Assert.True(range.TryValidateDuration(TimeSpan.FromTicks(durationTicks), out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000000)]
    [InlineData(104999999)]
    public void RejectsEndBeyondKnownDurationWithoutClamping(long durationTicks)
    {
        var range = Range("00:00:10:00", "00:00:10:15");
        Assert.False(range.TryValidateDuration(TimeSpan.FromTicks(durationTicks), out var error));
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("End", error.Field);
        Assert.Equal("00:00:10:15", range.End.ToString());
    }

    [Fact]
    public void RejectsNegativeKnownDuration()
    {
        var range = Range("00:00:00:00", "00:00:01:00");
        Assert.False(range.TryValidateDuration(TimeSpan.FromTicks(-1), out var error));
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("Duration", error.Field);
    }

    [Fact]
    public void DoesNotHideLengthOverflowWhenEndRoundsDownToDuration()
    {
        var range = Range("00:00:00:00", "00:00:00:05", 20000000);
        Assert.True(range.End.TryToTimeSpan(out var roundedEnd, out _));
        Assert.Equal(2, roundedEnd.Ticks);
        Assert.False(range.TryValidateDuration(roundedEnd, out var error));
        Assert.Equal("End", error.Field);
        Assert.True(range.TryValidateDuration(TimeSpan.FromTicks(3), out var validError));
        Assert.Null(validError);
    }

    [Fact]
    public void FractionalRateDurationComparisonUsesExactValueInsteadOfRoundedTicks()
    {
        var range = Range("00:00:00:00", "00:00:00:01", 30000, 1001);
        // One frame is 1001/30000 seconds, or 333666 and 2/3 ticks.
        Assert.False(range.TryValidateDuration(TimeSpan.FromTicks(333666), out var error));
        Assert.Equal("End", error.Field);
        Assert.True(range.TryValidateDuration(TimeSpan.FromTicks(333667), out var validError));
        Assert.Null(validError);
    }

    [Fact]
    public void HandlesMaximumRepresentableDurationWithoutIntermediateOverflow()
    {
        var range = Range("256204778:48:05:4775806", "256204778:48:05:4775807", 10000000);
        Assert.True(range.TryValidateDuration(TimeSpan.MaxValue, out var error));
        Assert.Null(error);
        Assert.False(range.TryValidateDuration(TimeSpan.FromTicks(long.MaxValue - 1), out var shorterError));
        Assert.Equal("End", shorterError.Field);
    }

    [Fact]
    public void NullEndpointsAreProgrammingErrors()
    {
        var time = Time("00:00:00:00");
        Assert.Throws<ArgumentNullException>(() => MediaTimeRange.TryCreate(null!, time, out _, out _));
        Assert.Throws<ArgumentNullException>(() => MediaTimeRange.TryCreate(time, null!, out _, out _));
    }
}
