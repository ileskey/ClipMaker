using System.Diagnostics.CodeAnalysis;

namespace ClipMaker.Media.Core.ValueObjects;

/// <summary>A signed half-open presentation interval [Start, End).</summary>
public sealed record PresentationRange
{
    public SignedMediaTime Start { get; }
    public SignedMediaTime End { get; }

    private PresentationRange(SignedMediaTime start, SignedMediaTime end)
    {
        Start = start;
        End = end;
    }

    public static bool TryCreate(SignedMediaTime start, SignedMediaTime end,
        [NotNullWhen(true)] out PresentationRange? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        value = null;
        error = null;
        if (end.CompareTo(start) <= 0)
        {
            error = new(TimeValidationErrorCode.OutOfRange, "End", "표시 구간의 끝은 시작보다 커야 합니다.");
            return false;
        }
        value = new(start, end);
        return true;
    }

    public bool Contains(SignedMediaTime time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return time.CompareTo(Start) >= 0 && time.CompareTo(End) < 0;
    }
}
