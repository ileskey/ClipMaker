namespace ClipDownloader;

public static class OutputFileNameSuggestion
{
    public static string Create(string? title, string? id, string start, string end)
    {
        string name = string.IsNullOrWhiteSpace(title) ? id ?? "clip" : title;
        name = new string(name.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        if (name.Length > 80)
        {
            int length = char.IsHighSurrogate(name[79]) ? 79 : 80;
            name = name[..length];
        }
        name = name.TrimEnd(' ', '.');
        if (name.Length == 0) name = "clip";
        string stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]))
            name = "_" + name;
        string CleanTime(string text) => new(text.Select(c => char.IsAsciiDigit(c) || c == ':' ? (c == ':' ? '-' : c) : '_').ToArray());
        return $"{name}_{CleanTime(start)}_{CleanTime(end)}.mp4";
    }
}
