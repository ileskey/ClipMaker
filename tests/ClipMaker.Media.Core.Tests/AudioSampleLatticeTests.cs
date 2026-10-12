using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class AudioSampleLatticeTests
{
    private static SignedMediaTime Time(long n, long d)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void RetainsNonzeroPhaseAndSignedSampleIndices(int rate)
    {
        var origin = Time(1, 96000);
        var lattice = new AudioSampleLattice(origin, rate);
        foreach (long n in new[] { long.MinValue, -1L, 0L, 1L, long.MaxValue })
        {
            var time = lattice.GetSampleTime(n);
            Assert.Equal(origin + Time(n, rate), time);
            Assert.True(lattice.TryGetSampleIndex(time, NativeTimestampRounding.Exact, out var index, out var error));
            Assert.Equal(n, index);
            Assert.Null(error);
        }
    }

    [Fact]
    public void CeilsRelativeToLatticeOriginRatherThanZeroOrFirstAvailableSample()
    {
        var lattice = new AudioSampleLattice(Time(1, 96000), 48000);
        Assert.True(lattice.TryGetSampleIndex(SignedMediaTime.Zero, NativeTimestampRounding.Ceiling, out var index, out _));
        Assert.Equal(0, index);
        Assert.Equal(Time(1, 96000), lattice.GetSampleTime(index));
        Assert.True(lattice.TryGetSampleIndex(SignedMediaTime.Zero, NativeTimestampRounding.Floor, out index, out _));
        Assert.Equal(-1, index);
        Assert.False(lattice.TryGetSampleIndex(SignedMediaTime.Zero, NativeTimestampRounding.Exact, out _, out _));
    }

    [Fact]
    public void ReproducesNativeBoundaryCountsWithoutClaimingCoverage()
    {
        var lattice = new AudioSampleLattice(SignedMediaTime.Zero, 48000);
        Assert.True(lattice.TryGetSampleIndex(Time(704705, 480000), NativeTimestampRounding.Ceiling, out var first, out _));
        Assert.True(lattice.TryGetSampleIndex(Time(1185185, 480000), NativeTimestampRounding.Ceiling, out var last, out _));
        Assert.Equal(70471, first);
        Assert.Equal(118519, last);
        Assert.Equal(48048, last - first);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidSampleRate(int rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleLattice(SignedMediaTime.Zero, rate));
}
