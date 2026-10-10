using System.Globalization;
using System.Text.RegularExpressions;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Diagnostics;

internal static partial class ToolOutputParser
{
    public static string? ReadVersion(ToolKind tool, string text)
    {
        string first = text.Split(['\r', '\n'], 2)[0].TrimStart('\uFEFF').Trim();
        if (tool == ToolKind.YtDlp)
        {
            if (first.Length > 128 || !YtDlpVersion().IsMatch(first) ||
                !DateOnly.TryParseExact(first[..10], "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;
            return first;
        }
        string prefix = tool == ToolKind.Ffmpeg ? "ffmpeg version " : "ffprobe version ";
        if (!first.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string token = first[prefix.Length..].Split(' ', 2)[0];
        return VersionToken().IsMatch(token) && !token.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? token : null;
    }

    public static HashSet<string>? ReadFeatures(ToolFeatureKind kind, string text)
    {
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (!lines.Any(line => kind == ToolFeatureKind.Encoder ? line.Trim() == "Encoders:" :
            line.Trim() is "File formats:" or "Formats:")) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            string flags = fields[0];
            bool valid = kind == ToolFeatureKind.Encoder
                ? flags.Length == 6 && flags[0] is 'V' or 'A' or 'S' && flags.All(character => char.IsAsciiLetterUpper(character) || character == '.')
                : flags.Length is 1 or 2 && flags.Contains('E') && flags.All(character => character is 'D' or 'E' or '.');
            if (!valid) continue;
            // FFmpeg 8 adds a separate 'd' marker for devices in its format listing.
            string identifiers = kind == ToolFeatureKind.Muxer && fields[1] == "d" && fields.Length > 2 ? fields[2] : fields[1];
            foreach (string name in identifiers.Split(','))
                if (FeatureName().IsMatch(name)) names.Add(name);
        }
        return names.Count == 0 ? null : names;
    }

    [GeneratedRegex(@"\A\d{4}\.\d{2}\.\d{2}(?:\.[A-Za-z0-9._+-]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex YtDlpVersion();
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionToken();
    [GeneratedRegex(@"\A[A-Za-z0-9_+.-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureName();
}
