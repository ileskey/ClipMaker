using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace ClipMaker.Media.Core.ValueObjects;

/// <summary>Exact signed seconds; conversion to a tool clock is explicit.</summary>
public sealed record SignedMediaTime : IComparable<SignedMediaTime>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public static SignedMediaTime Zero { get; } = new(0, 1);

    private SignedMediaTime(BigInteger numerator, BigInteger denominator)
    {
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public static bool TryCreate(BigInteger numerator, BigInteger denominator,
        [NotNullWhen(true)] out SignedMediaTime? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        value = null;
        error = null;
        if (denominator <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "Denominator", "시간의 분모는 1 이상이어야 합니다.");
            return false;
        }
        value = new(numerator, denominator);
        return true;
    }

    public static SignedMediaTime FromTimecode(Timecode timecode)
    {
        ArgumentNullException.ThrowIfNull(timecode);
        var input = timecode.Input;
        var rate = timecode.FrameRate;
        BigInteger wholeSeconds = (BigInteger)input.Hours * 3600 + input.Minutes * 60 + input.Seconds;
        return new(wholeSeconds * rate.Numerator + (BigInteger)input.Frames * rate.Denominator, rate.Numerator);
    }

    public static SignedMediaTime operator +(SignedMediaTime left, SignedMediaTime right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new(left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);
    }

    public static SignedMediaTime operator -(SignedMediaTime left, SignedMediaTime right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new(left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);
    }

    public int CompareTo(SignedMediaTime? other) => other is null ? 1 :
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool TryToNativeTicks(SignedMediaTime timeBase, NativeTimestampRounding rounding,
        out long ticks, [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(timeBase);
        if (!Enum.IsDefined(rounding)) throw new ArgumentOutOfRangeException(nameof(rounding));
        ticks = 0;
        error = null;
        if (timeBase.Numerator <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "TimeBase", "time_base는 0보다 커야 합니다.");
            return false;
        }

        BigInteger quotient = BigInteger.DivRem(Numerator * timeBase.Denominator,
            Denominator * timeBase.Numerator, out var remainder);
        if (rounding == NativeTimestampRounding.Exact && !remainder.IsZero)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "Timestamp", "시간을 이 time_base의 정수 PTS로 정확히 표현할 수 없습니다.");
            return false;
        }
        if (rounding == NativeTimestampRounding.Floor && remainder.Sign < 0) quotient--;
        if (rounding == NativeTimestampRounding.Ceiling && remainder.Sign > 0) quotient++;
        if (quotient < long.MinValue || quotient > long.MaxValue)
        {
            error = new(TimeValidationErrorCode.Overflow, "Timestamp", "변환된 PTS가 Int64 표현 범위를 넘습니다.");
            return false;
        }
        ticks = (long)quotient;
        return true;
    }
}
