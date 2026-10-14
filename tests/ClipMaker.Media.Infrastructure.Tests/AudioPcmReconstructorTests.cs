using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Processing;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class AudioPcmReconstructorTests
{
    [Theory]
    [InlineData(1, 0, 11)]
    [InlineData(2, 2, 10)]
    [InlineData(2, -3, 15)]
    public async Task ValuesMatchIndependentFirstWinsGridIncludingBoundariesAndInternalGap(int channels, int first, int end)
    {
        SignedMediaTime.TryCreate(1, 7, out var origin, out _);
        var lattice = new AudioSampleLattice(origin!, 48000);
        var boundary = new AudioBoundaryPlan(lattice, first, end, 0, 11, lattice.GetSampleTime(first));
        AudioPcmFramePlacement[] frames = [new(0, 0, 4), new(4, 3, 3), new(7, 8, 3)];
        var plan = new AudioPcmReconstructionPlan(boundary, channels, 10, frames);
        byte[] values = Enumerable.Range(0, 10 * channels * 4).Select(i => (byte)(i + 1)).ToArray();
        var oracle = new byte[11 * channels * 4]; var occupied = new bool[11];
        foreach (var frame in frames)
            for (int i = 0; i < frame.Samples; i++)
                if (!occupied[frame.LatticeFirst + i])
                {
                    occupied[frame.LatticeFirst + i] = true;
                    Array.Copy(values, (frame.DecodedFirst + i) * channels * 4, oracle, (frame.LatticeFirst + i) * channels * 4, channels * 4);
                }
        using var input = new MemoryStream(values, false); using var output = new MemoryStream();
        var counts = await new AudioPcmReconstructor().ReconstructAsync(plan, input, output, TestContext.Current.CancellationToken);
        Assert.Equal(oracle.Skip((int)boundary.SelectedFirst * channels * 4).Take((int)(boundary.SelectedEnd - boundary.SelectedFirst) * channels * 4), output.ToArray());
        Assert.Equal(boundary.SelectedEnd - boundary.SelectedFirst, counts.WrittenSamples);
        Assert.Equal(2, counts.InsertedZeroSamples); Assert.Equal(1, counts.DiscardedOverlapSamples);
        Assert.Equal(lattice.GetSampleTime(first), boundary.RenderOrigin);
    }

    [Fact]
    public async Task ChangedPcmOrCancelledReconstructionDoesNotReturnEvidence()
    {
        var lattice = new AudioSampleLattice(SignedMediaTime.Zero, 48000);
        var boundary = new AudioBoundaryPlan(lattice, 0, 4, 0, 4, SignedMediaTime.Zero);
        var plan = new AudioPcmReconstructionPlan(boundary, 1, 4, [new(0, 0, 4)]);
        using var input = new MemoryStream(new byte[15]); using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AudioPcmReconstructor().ReconstructAsync(plan, input, output, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var exact = new MemoryStream(new byte[16]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioPcmReconstructor().ReconstructAsync(plan, exact, output, cancelled.Token));
        Assert.Empty(output.ToArray());
        Assert.Throws<ArgumentException>(() => new AudioPcmReconstructionPlan(boundary, 1, 4, [new(0, 1, 3)]));
    }
}
