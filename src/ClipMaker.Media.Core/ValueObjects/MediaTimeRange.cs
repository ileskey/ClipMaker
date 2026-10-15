using System.Diagnostics.CodeAnalysis;

namespace ClipMaker.Media.Core.ValueObjects;

/// <summary>A validated half-open [Start, End) range using a single frame rate.</summary>
public sealed record MediaTimeRange
{
    public Timecode Start { get; }
    public Timecode End { get; }
    public FrameRate FrameRate => Start.FrameRate;

    private MediaTimeRange(Timecode start, Timecode end)
    {
        Start = start;
        End = end;
    }

    public static bool TryCreate(Timecode start, Timecode end,
        [NotNullWhen(true)] out MediaTimeRange? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        value = null;
        error = null;
        if (start.FrameRate != end.FrameRate)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "FrameRate",
                "시작과 종료 타임코드는 같은 FPS여야 합니다.");
            return false;
        }

        // Validated Timecode values already guarantee Start >= 0.
        // Compare exact values: rounding to ticks can collapse a valid short range.
        if (end.CompareTo(start) <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "End",
                "종료 시간은 시작 시간보다 커야 합니다.");
            return false;
        }

        value = new(start, end);
        return true;
    }

    /// <summary>
    /// Compares the exact end with a known duration in the same time coordinate system.
    /// The caller supplies the duration; this model does not retrieve or infer it.
    /// </summary>
    public bool TryValidateDuration(TimeSpan duration,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        error = null;
        if (duration < TimeSpan.Zero)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "Duration",
                "영상 길이는 0 이상이어야 합니다.");
            return false;
        }
        if (End.CompareTo(duration) > 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "End",
                "종료 시간은 영상 전체 길이를 넘을 수 없습니다.");
            return false;
        }
        return true;
    }
}
