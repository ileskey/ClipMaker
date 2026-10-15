using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

/// <summary>A constant demux-to-native correction, valid only in its inspection fact's scope.</summary>
public sealed record NativeTimeRelation
{
    public SignedMediaTime ObservedTimeBase { get; }
    public SignedMediaTime Correction { get; }

    public NativeTimeRelation(SignedMediaTime observedTimeBase, SignedMediaTime correction)
    {
        ArgumentNullException.ThrowIfNull(observedTimeBase);
        ArgumentNullException.ThrowIfNull(correction);
        if (observedTimeBase.Numerator <= 0) throw new ArgumentOutOfRangeException(nameof(observedTimeBase));
        ObservedTimeBase = observedTimeBase;
        Correction = correction;
    }

    public SignedMediaTime GetNativeTime(NativeTimestamp observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (observed.TimeBase != ObservedTimeBase)
            throw new ArgumentException("Observed timestamp uses a different time base.", nameof(observed));
        return observed.PresentationTime + Correction;
    }
}
