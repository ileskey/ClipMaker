using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ClipMaker.Media.Core.ValueObjects;

public sealed record TimecodeInput
{
    public long Hours { get; }
    public int Minutes { get; }
    public int Seconds { get; }
    public long Frames { get; }

    private TimecodeInput(long hours, int minutes, int seconds, long frames)
    {
        Hours = hours;
        Minutes = minutes;
        Seconds = seconds;
        Frames = frames;
    }

    // Parsing preserves syntax-valid components; semantic validation needs the selected FPS.
    public static bool TryParse(string? text,
        [NotNullWhen(true)] out TimecodeInput? value,
        [NotNullWhen(false)] out TimeValidationError? error)
    {
        value = null;
        error = null;
        var fields = text?.Trim().Split(':');
        if (fields is null || fields.Length != 4)
        {
            error = new(TimeValidationErrorCode.InvalidSyntax, "Timecode",
                "HH:MM:SS:FF 네 필드를 입력해야 합니다.");
            return false;
        }

        string[] names = ["Hours", "Minutes", "Seconds", "Frames"];
        var components = new long[4];
        for (int index = 0; index < fields.Length; index++)
        {
            string field = fields[index];
            bool validLength = index is 1 or 2 ? field.Length == 2 : field.Length >= 2;
            if (!validLength || field.Any(character => character is < '0' or > '9'))
            {
                error = new(TimeValidationErrorCode.InvalidSyntax, names[index],
                    index is 1 or 2
                        ? "분·초는 ASCII 숫자 2자리여야 합니다."
                        : "시간·프레임은 ASCII 숫자 최소 2자리여야 합니다.");
                return false;
            }
            if (!long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out components[index]))
            {
                error = new(TimeValidationErrorCode.Overflow, names[index],
                    $"입력값은 0~{long.MaxValue.ToString(CultureInfo.InvariantCulture)} 범위여야 합니다.");
                return false;
            }
        }

        value = new(components[0], (int)components[1], (int)components[2], components[3]);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Hours:D2}:{Minutes:D2}:{Seconds:D2}:{Frames:D2}");
}
