using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace ClipMaker.Media.Core.ValueObjects;

public sealed record FrameRate
{
    public long Numerator { get; }
    public long Denominator { get; }

    private FrameRate(long numerator, long denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public static bool TryCreate(long numerator, long denominator,
        [NotNullWhen(true)] out FrameRate? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        value = null;
        error = null;
        if (numerator <= 0 || denominator <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "FrameRate",
                "FPS의 분자와 분모는 각각 1 이상이어야 합니다.");
            return false;
        }

        long divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
        value = new(numerator / divisor, denominator / divisor);
        return true;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}
