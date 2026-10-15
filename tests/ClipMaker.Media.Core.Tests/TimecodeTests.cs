using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class TimecodeTests
{
    private static FrameRate Rate(long numerator, long denominator = 1)
    {
        Assert.True(FrameRate.TryCreate(numerator, denominator, out var rate, out var error));
        Assert.Null(error);
        return rate;
    }

    private static TimecodeInput Input(string text)
    {
        Assert.True(TimecodeInput.TryParse(text, out var input, out var error));
        Assert.Null(error);
        return input;
    }

    private static Timecode Time(string text, long numerator, long denominator = 1)
    {
        Assert.True(Timecode.TryCreate(Input(text), Rate(numerator, denominator), out var time, out var error));
        Assert.Null(error);
        return time;
    }

    [Theory]
    [InlineData("00:00:10:15", "00:00:10:15")]
    [InlineData(" \t00:00:10:15\r\n", "00:00:10:15")]
    [InlineData("100:00:00:00", "100:00:00:00")]
    [InlineData("0000:01:02:003", "00:01:02:03")]
    [InlineData("00:00:00:100", "00:00:00:100")]
    [InlineData("00:99:99:00", "00:99:99:00")]
    public void SyntaxParsingPreservesComponentsWithoutFps(string text, string normalized)
    {
        var input = Input(text);
        Assert.Equal(normalized, input.ToString());
        Assert.Equal(input, Input(normalized));
    }

    [Theory]
    [InlineData(null, "Timecode")]
    [InlineData("", "Timecode")]
    [InlineData("00:00:00", "Timecode")]
    [InlineData("00:00:00:00:00", "Timecode")]
    [InlineData("0:00:10:00", "Hours")]
    [InlineData("00:0:10:00", "Minutes")]
    [InlineData("00:00:1:00", "Seconds")]
    [InlineData("00:00:10:0", "Frames")]
    [InlineData("00:000:10:00", "Minutes")]
    [InlineData("+0:00:10:00", "Hours")]
    [InlineData("-1:00:10:00", "Hours")]
    [InlineData("00:00:10:1.5", "Frames")]
    [InlineData("00:00:10;15", "Timecode")]
    [InlineData("00:00: 10:15", "Seconds")]
    [InlineData("００:00:10:15", "Hours")]
    [InlineData("00:00:10:١٥", "Frames")]
    [InlineData("00:00:\n10:15", "Seconds")]
    public void RejectsInvalidSyntax(string? text, string field)
    {
        Assert.False(TimecodeInput.TryParse(text, out var input, out var error));
        Assert.Null(input);
        Assert.Equal(TimeValidationErrorCode.InvalidSyntax, error.Code);
        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData("9223372036854775808:00:00:00", "Hours")]
    [InlineData("00:00:00:9223372036854775808", "Frames")]
    public void ReportsComponentOverflow(string text, string field)
    {
        Assert.False(TimecodeInput.TryParse(text, out var input, out var error));
        Assert.Null(input);
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData("00:60:00:00", 30, 1, "Minutes")]
    [InlineData("00:00:60:00", 30, 1, "Seconds")]
    [InlineData("00:00:00:30", 30, 1, "Frames")]
    [InlineData("00:00:00:30", 2997, 100, "Frames")]
    [InlineData("00:00:00:60", 60000, 1001, "Frames")]
    [InlineData("00:00:00:01", 1, 2, "Frames")]
    public void SemanticValidationUsesActualRationalFps(
        string text, long numerator, long denominator, string field)
    {
        Assert.False(Timecode.TryCreate(Input(text), Rate(numerator, denominator), out var time, out var error));
        Assert.Null(time);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData("00:00:10:15", 30, 1, 105000000, 315)]
    [InlineData("00:00:10:15", 2997, 100, 105005005, 314)]
    [InlineData("00:00:10:15", 30000, 1001, 105005000, 314)]
    [InlineData("00:00:00:29", 2997, 100, 9676343, 29)]
    [InlineData("00:00:00:59", 60000, 1001, 9843167, 59)]
    [InlineData("00:00:00:100", 120, 1, 8333333, 100)]
    [InlineData("100:00:00:00", 30, 1, 3600000000000, 10800000)]
    [InlineData("00:00:00:00", 1, 2, 0, 0)]
    public void ConvertsClockTimeAndContinuousFramesExactly(string text, long numerator,
        long denominator, long expectedTicks, long expectedFrames)
    {
        var time = Time(text, numerator, denominator);
        Assert.True(time.TryToTimeSpan(out var duration, out var timeError));
        Assert.Null(timeError);
        Assert.Equal(expectedTicks, duration.Ticks);
        Assert.True(time.TryToFrameNumber(out var frames, out var frameError));
        Assert.Null(frameError);
        Assert.Equal(expectedFrames, frames);
        Assert.Equal(text, time.ToString());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    [InlineData(5, 2)]
    [InlineData(7, 4)]
    public void RoundsHalfTicksToEven(long frameOffset, long expectedTicks)
    {
        var time = Time($"00:00:00:{frameOffset:D2}", 20000000);
        Assert.True(time.TryToTimeSpan(out var duration, out _));
        Assert.Equal(expectedTicks, duration.Ticks);
    }

    [Fact]
    public void ComparesExactTimesEvenWhenTheyRoundToTheSameTick()
    {
        var zero = Time("00:00:00:00", 20000000);
        var halfTick = Time("00:00:00:01", 20000000);
        Assert.True(zero.TryToTimeSpan(out var roundedZero, out _));
        Assert.True(halfTick.TryToTimeSpan(out var roundedHalf, out _));
        Assert.Equal(roundedZero, roundedHalf);
        Assert.True(halfTick.CompareTo(zero) > 0);
        Assert.True(halfTick.CompareTo(TimeSpan.Zero) > 0);
        Assert.True(zero.CompareTo(halfTick) < 0);
    }

    [Fact]
    public void ComparesAcrossRatesWithoutLosingExactValue()
    {
        Assert.Equal(0, Time("00:00:10:15", 30).CompareTo(Time("00:00:10:30", 60)));
        Assert.True(Time("00:00:10:15", 2997, 100).CompareTo(Time("00:00:10:15", 30000, 1001)) > 0);
    }

    [Fact]
    public void AcceptsMaxTimeSpanAndRejectsOneTickBeyondIt()
    {
        var maximum = Time("256204778:48:05:4775807", 10000000);
        Assert.True(maximum.TryToTimeSpan(out var duration, out _));
        Assert.Equal(TimeSpan.MaxValue, duration);
        Assert.Equal(0, maximum.CompareTo(TimeSpan.MaxValue));
        Assert.False(Timecode.TryCreate(Input("256204778:48:05:4775808"), Rate(10000000), out var beyond, out var error));
        Assert.Null(beyond);
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
    }

    [Fact]
    public void DoesNotHideOutOfRangeExactTimeByRoundingDown()
    {
        Assert.False(Timecode.TryCreate(Input("256204778:48:05:9551615"), Rate(20000000), out var time, out var error));
        Assert.Null(time);
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
    }

    [Fact]
    public void RejectsHugeHoursWithoutIntermediateIntegerOverflow()
    {
        Assert.False(Timecode.TryCreate(Input("9223372036854775807:00:00:00"), Rate(long.MaxValue), out var time, out var error));
        Assert.Null(time);
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
    }

    [Fact]
    public void BigIntegerIntermediateSupportsMaximumRateAndSeparatelyChecksFrameNumberOverflow()
    {
        var lastFrameOfSecond = Time("00:00:00:9223372036854775806", long.MaxValue);
        Assert.True(lastFrameOfSecond.TryToTimeSpan(out var duration, out _));
        Assert.Equal(TimeSpan.FromSeconds(1), duration);
        Assert.True(lastFrameOfSecond.TryToFrameNumber(out var number, out _));
        Assert.Equal(long.MaxValue - 1, number);

        var twoSeconds = Time("00:00:02:00", long.MaxValue);
        Assert.False(twoSeconds.TryToFrameNumber(out _, out var error));
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
        Assert.Equal("FrameNumber", error.Field);
    }

    [Fact]
    public void RevalidatesSameInputWhenSelectedRateChanges()
    {
        var input = Input("00:00:00:29");
        Assert.True(Timecode.TryCreate(input, Rate(30), out var original, out _));
        Assert.False(Timecode.TryCreate(input, Rate(25), out var invalid, out var error));
        Assert.Null(invalid);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal(30, original.FrameRate.Numerator);
        Assert.Equal(29, original.Input.Frames);
    }
}
