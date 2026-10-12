using System.Numerics;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class SignedMediaTimeTests
{
    private static SignedMediaTime Time(BigInteger n, BigInteger d)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out var error));
        Assert.Null(error);
        return time;
    }

    [Theory]
    [InlineData(-6, 8, -3, 4)]
    [InlineData(6, 8, 3, 4)]
    [InlineData(0, 8, 0, 1)]
    public void CanonicalizesSignedRationals(long n, long d, long expectedN, long expectedD)
    {
        var time = Time(n, d);
        Assert.Equal(new BigInteger(expectedN), time.Numerator);
        Assert.Equal(new BigInteger(expectedD), time.Denominator);
        Assert.Equal(Time(expectedN, expectedD), time);
        Assert.Equal(Time(expectedN, expectedD).GetHashCode(), time.GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveDenominator(long denominator)
    {
        Assert.False(SignedMediaTime.TryCreate(-1, denominator, out var time, out var error));
        Assert.Null(time);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("Denominator", error.Field);
    }

    [Fact]
    public void ArithmeticAndComparisonRemainExactBeyondDoubleAndInt64()
    {
        BigInteger large = BigInteger.Pow(2, 100);
        var whole = Time(large, 1);
        var fraction = Time(1, 3);
        var sum = whole + fraction;
        Assert.Equal(large * 3 + 1, sum.Numerator);
        Assert.Equal(new BigInteger(3), sum.Denominator);
        Assert.Equal(fraction, sum - whole);
        Assert.True(sum.CompareTo(whole) > 0);
        Assert.True(Time(-1, 3).CompareTo(SignedMediaTime.Zero) < 0);
        Assert.Equal(SignedMediaTime.Zero, fraction - fraction);
        Assert.Equal(1, whole.CompareTo(null));
    }

    [Fact]
    public void BridgesTimecodeWithoutTimeSpanRoundingOrStandardRateGuess()
    {
        Assert.True(TimecodeInput.TryParse("00:00:00:01", out var input, out _));
        Assert.True(FrameRate.TryCreate(30000, 1001, out var nativeRate, out _));
        Assert.True(FrameRate.TryCreate(2997, 100, out var decimalRate, out _));
        Assert.True(Timecode.TryCreate(input, nativeRate, out var native, out _));
        Assert.True(Timecode.TryCreate(input, decimalRate, out var declared, out _));
        var exact = SignedMediaTime.FromTimecode(native);
        Assert.Equal(Time(1001, 30000), exact);
        Assert.Equal(Time(100, 2997), SignedMediaTime.FromTimecode(declared));
        Assert.NotEqual(exact, SignedMediaTime.FromTimecode(declared));
        Assert.True(native.TryToTimeSpan(out var span, out _));
        Assert.NotEqual(exact, Time(span.Ticks, TimeSpan.TicksPerSecond));
    }

    [Fact]
    public void BridgesClockComponentsAndFrameOffset()
    {
        Assert.True(TimecodeInput.TryParse("100:02:03:15", out var input, out _));
        Assert.True(FrameRate.TryCreate(30, 1, out var rate, out _));
        Assert.True(Timecode.TryCreate(input, rate, out var timecode, out _));
        Assert.Equal(Time(720247, 2), SignedMediaTime.FromTimecode(timecode));
    }

    [Theory]
    [InlineData(-3, -2, -1)]
    [InlineData(-1, -1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(3, 1, 2)]
    public void NativeFloorAndCeilingUseMathematicalSignedRounding(long n, long floor, long ceiling)
    {
        var time = Time(n, 2);
        Assert.True(time.TryToNativeTicks(Time(1, 1), NativeTimestampRounding.Floor, out var f, out var error));
        Assert.Null(error);
        Assert.Equal(floor, f);
        Assert.True(time.TryToNativeTicks(Time(1, 1), NativeTimestampRounding.Ceiling, out var c, out error));
        Assert.Null(error);
        Assert.Equal(ceiling, c);
    }

    [Fact]
    public void ExactConversionRejectsFractionRatherThanSilentlyRounding()
    {
        Assert.False(Time(-1, 96000).TryToNativeTicks(Time(1, 48000), NativeTimestampRounding.Exact,
            out var ticks, out var error));
        Assert.Equal(0, ticks);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Equal("Timestamp", error.Field);
        Assert.True(Time(-1, 48000).TryToNativeTicks(Time(1, 48000), NativeTimestampRounding.Exact, out ticks, out error));
        Assert.Equal(-1, ticks);
        Assert.Null(error);
    }

    [Fact]
    public void NativeConversionChecksInt64AfterExplicitRounding()
    {
        var unit = Time(1, 1);
        foreach (long edge in new[] { long.MinValue, long.MaxValue })
        {
            Assert.True(Time(edge, 1).TryToNativeTicks(unit, NativeTimestampRounding.Exact, out var ticks, out _));
            Assert.Equal(edge, ticks);
        }
        var above = Time((BigInteger)long.MaxValue * 2 + 1, 2);
        Assert.True(above.TryToNativeTicks(unit, NativeTimestampRounding.Floor, out var max, out _));
        Assert.Equal(long.MaxValue, max);
        Assert.False(above.TryToNativeTicks(unit, NativeTimestampRounding.Ceiling, out var failed, out var error));
        Assert.Equal(0, failed);
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
        var below = Time((BigInteger)long.MinValue * 2 - 1, 2);
        Assert.True(below.TryToNativeTicks(unit, NativeTimestampRounding.Ceiling, out var min, out _));
        Assert.Equal(long.MinValue, min);
        Assert.False(below.TryToNativeTicks(unit, NativeTimestampRounding.Floor, out _, out error));
        Assert.Equal(TimeValidationErrorCode.Overflow, error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveNativeTimeBase(long n)
    {
        Assert.False(Time(1, 1).TryToNativeTicks(Time(n, 1), NativeTimestampRounding.Exact, out var ticks, out var error));
        Assert.Equal(0, ticks);
        Assert.Equal("TimeBase", error.Field);
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
    }

    [Theory]
    [InlineData(95935, 4380000, 95935)]
    [InlineData(99007, 4383192, 95815)]
    public void ReproducesG2SourceLocalAndCommonRenderCoordinates(long origin, long videoOffset, long localIn)
    {
        var sourceIn = Time(365, 4) + Time(origin, 48000);
        Assert.Equal(Time(localIn, 48000), sourceIn - Time(videoOffset, 48000));
        Assert.Equal(SignedMediaTime.Zero, Time(localIn, 48000) + Time(videoOffset, 48000) - sourceIn);
    }

    [Theory]
    [InlineData(704705, 1185185, 5)]
    [InlineData(704706, 1185188, 4)]
    public void ReproducesG3cNativeCeilBoundariesAndCalculatedRenderResidual(long start, long end, long residual)
    {
        var clock = Time(1, 48000);
        var first = Time(start, 480000);
        Assert.True(first.TryToNativeTicks(clock, NativeTimestampRounding.Ceiling, out var nIn, out _));
        Assert.True(Time(end, 480000).TryToNativeTicks(clock, NativeTimestampRounding.Ceiling, out var nOut, out _));
        Assert.Equal(70471, nIn);
        Assert.Equal(118519, nOut);
        Assert.Equal(48048, nOut - nIn);
        Assert.Equal(Time(residual, 480000), Time(nIn, 48000) - first);
    }

    [Fact]
    public void GuardsProgrammingErrors()
    {
        Assert.Throws<ArgumentNullException>(() => SignedMediaTime.FromTimecode(null!));
        Assert.Throws<ArgumentNullException>(() => SignedMediaTime.Zero + null!);
        Assert.Throws<ArgumentNullException>(() => null! - SignedMediaTime.Zero);
        Assert.Throws<ArgumentNullException>(() => SignedMediaTime.Zero.TryToNativeTicks(null!, NativeTimestampRounding.Exact, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => SignedMediaTime.Zero.TryToNativeTicks(Time(1, 1), (NativeTimestampRounding)99, out _, out _));
    }
}
