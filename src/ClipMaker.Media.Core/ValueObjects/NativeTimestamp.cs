using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace ClipMaker.Media.Core.ValueObjects;

public enum NativeTimestampRounding { Exact, Floor, Ceiling }

/// <summary>An observed signed PTS with its positive native time base.</summary>
public sealed record NativeTimestamp
{
    public long Pts { get; }
    public SignedMediaTime TimeBase { get; }
    public SignedMediaTime PresentationTime { get; }

    private NativeTimestamp(long pts, SignedMediaTime timeBase)
    {
        Pts = pts;
        TimeBase = timeBase;
        // The validated positive time base guarantees a positive denominator.
        SignedMediaTime.TryCreate((BigInteger)pts * timeBase.Numerator, timeBase.Denominator, out var time, out _);
        PresentationTime = time!;
    }

    public static bool TryCreate(long pts, SignedMediaTime timeBase,
        [NotNullWhen(true)] out NativeTimestamp? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(timeBase);
        value = null;
        error = null;
        if (timeBase.Numerator <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "TimeBase", "time_base는 0보다 커야 합니다.");
            return false;
        }
        value = new(pts, timeBase);
        return true;
    }
}
