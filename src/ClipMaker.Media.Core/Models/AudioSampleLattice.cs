using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

/// <summary>Native sample time G + n/R; this does not establish coverage or valid support.</summary>
public sealed record AudioSampleLattice
{
    public SignedMediaTime Origin { get; }
    public int SampleRate { get; }
    private readonly SignedMediaTime sampleTimeBase;

    public AudioSampleLattice(SignedMediaTime origin, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        Origin = origin;
        SampleRate = sampleRate;
        SignedMediaTime.TryCreate(1, sampleRate, out var timeBase, out _);
        sampleTimeBase = timeBase!;
    }

    public bool TryGetSampleIndex(SignedMediaTime time, NativeTimestampRounding rounding,
        out long index, [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(time);
        return (time - Origin).TryToNativeTicks(sampleTimeBase, rounding, out index, out error);
    }

    public SignedMediaTime GetSampleTime(long index)
    {
        SignedMediaTime.TryCreate(new BigInteger(index), SampleRate, out var relative, out _);
        return Origin + relative!;
    }
}
