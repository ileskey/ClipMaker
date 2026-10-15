using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace ClipMaker.Media.Core.ValueObjects;

public sealed record Timecode : IComparable<Timecode>
{
    public TimecodeInput Input { get; }
    public FrameRate FrameRate { get; }

    // Exact seconds are retained until the TimeSpan boundary.
    private readonly BigInteger secondsNumerator;
    private readonly BigInteger secondsDenominator;

    private Timecode(TimecodeInput input, FrameRate frameRate)
    {
        Input = input;
        FrameRate = frameRate;
        BigInteger wholeSeconds = (BigInteger)input.Hours * 3600 + input.Minutes * 60 + input.Seconds;
        secondsNumerator = wholeSeconds * frameRate.Numerator + (BigInteger)input.Frames * frameRate.Denominator;
        secondsDenominator = frameRate.Numerator;
    }

    public static bool TryCreate(TimecodeInput input, FrameRate frameRate,
        [NotNullWhen(true)] out Timecode? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frameRate);
        value = null;
        error = null;
        if (input.Minutes > 59 || input.Seconds > 59)
        {
            error = new(TimeValidationErrorCode.OutOfRange, input.Minutes > 59 ? "Minutes" : "Seconds",
                "분·초는 00~59 범위여야 합니다.");
            return false;
        }
        if ((BigInteger)input.Frames * frameRate.Denominator >= frameRate.Numerator)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "Frames",
                $"프레임은 0 이상이고 실제 FPS({frameRate}) 미만이어야 합니다.");
            return false;
        }

        var candidate = new Timecode(input, frameRate);
        if (!candidate.TryToTimeSpan(out _, out error)) return false;
        value = candidate;
        return true;
    }

    public bool TryToTimeSpan(out TimeSpan value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        value = default;
        error = null;
        BigInteger tickNumerator = secondsNumerator * TimeSpan.TicksPerSecond;
        // Reject values beyond the representable duration even if rounding could hide the excess.
        if (tickNumerator > (BigInteger)long.MaxValue * secondsDenominator)
        {
            error = new(TimeValidationErrorCode.Overflow, "Timecode",
                "시간이 TimeSpan의 최대 표현 범위를 넘었습니다.");
            return false;
        }

        BigInteger ticks = BigInteger.DivRem(tickNumerator, secondsDenominator, out var remainder);
        int midpoint = (remainder * 2).CompareTo(secondsDenominator);
        if (midpoint > 0 || (midpoint == 0 && !ticks.IsEven)) ticks++;
        value = TimeSpan.FromTicks((long)ticks);
        return true;
    }

    public bool TryToFrameNumber(out long value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        value = default;
        error = null;
        // FrameNumber is continuous, unlike the clock-time + frame-offset input notation.
        BigInteger frames = secondsNumerator * FrameRate.Numerator /
            (secondsDenominator * FrameRate.Denominator);
        if (frames > long.MaxValue)
        {
            error = new(TimeValidationErrorCode.Overflow, "FrameNumber",
                "연속 프레임 번호가 Int64의 최대 표현 범위를 넘었습니다.");
            return false;
        }
        value = (long)frames;
        return true;
    }

    public int CompareTo(Timecode? other) => other is null ? 1 :
        (secondsNumerator * other.secondsDenominator).CompareTo(other.secondsNumerator * secondsDenominator);

    public int CompareTo(TimeSpan duration) => (secondsNumerator * TimeSpan.TicksPerSecond)
        .CompareTo((BigInteger)duration.Ticks * secondsDenominator);

    public override string ToString() => Input.ToString();
}
