using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class NativeTimeRelationTests
{
    private static SignedMediaTime Time(long n, long d)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }

    [Fact]
    public void ComputesMeasuredOpusCorrectionWithoutInventingUniversalOffset()
    {
        var observedClock = Time(1, 1000);
        Assert.True(NativeTimestamp.TryCreate(-7, observedClock, out var packet, out _));
        var measured = new NativeTimeRelation(observedClock, Time(24, 48000));
        Assert.Equal(Time(-312, 48000), measured.GetNativeTime(packet));
        var zeroCorrection = new NativeTimeRelation(observedClock, SignedMediaTime.Zero);
        Assert.Equal(Time(-336, 48000), zeroCorrection.GetNativeTime(packet));
    }

    [Fact]
    public void ChangedLocalBasisRequiresRebasedSourceOffset()
    {
        var clock = Time(1, 48000);
        Assert.True(NativeTimestamp.TryCreate(95935, clock, out var local, out _));
        var source = Time(4475935, 48000);
        var correction = Time(24, 48000);
        var native = new NativeTimeRelation(clock, correction).GetNativeTime(local);
        var oldB = Time(4380000, 48000);
        Assert.Equal(source, native + (oldB - correction));
        Assert.NotEqual(source, native + oldB);
    }

    [Fact]
    public void RejectsUnitsMismatchAndDoesNotSilentlyQuantizeCorrection()
    {
        var relation = new NativeTimeRelation(Time(1, 1000), Time(1, 96000));
        Assert.True(NativeTimestamp.TryCreate(0, Time(1, 48000), out var wrong, out _));
        Assert.Throws<ArgumentException>(() => relation.GetNativeTime(wrong));
        Assert.True(NativeTimestamp.TryCreate(0, Time(1, 1000), out var observed, out _));
        Assert.False(relation.GetNativeTime(observed).TryToNativeTicks(Time(1, 48000), NativeTimestampRounding.Exact, out _, out var error));
        Assert.Equal(TimeValidationErrorCode.OutOfRange, error.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeTimeRelation(SignedMediaTime.Zero, SignedMediaTime.Zero));
    }
}
