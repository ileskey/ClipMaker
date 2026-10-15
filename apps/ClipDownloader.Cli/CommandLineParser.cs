using System.Globalization;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Sources;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Cli;

public sealed record CliCommand(string Name, Uri? Url, string? Start, string? End, string? Output,
    QualityOption Quality, ClipMode Mode, bool Yes, IReadOnlyDictionary<string, string> ToolPaths);
public sealed record CommandParseResult(CliCommand? Command, bool Help, string? Error);

public static class CommandLineParser
{
    public const string HelpText = """
        ClipDownloader CLI
          diagnose [tool options]
          info --url URL [tool options]
          clip --url URL --start HH:MM:SS:FF --end HH:MM:SS:FF --output FILE.mp4
               [--quality best|720p] [--mode fast|accurate] [--yes] [tool options]
          --help | COMMAND --help
        Tool options: --yt-dlp-path FILE --ffmpeg-path FILE --ffprobe-path FILE
        YouTube requires an existing Node.js 22+ on the tool search path; no automatic installation.
        Defaults: best, fast. Existing output directories only; never overwrite.
        Source budget: 64 MiB / 60 seconds per bounded acquisition (including source-start references).
        HLS whole segments and reference margins count toward this budget; a 30-second clip may exceed it.
        Redirected stdin requires --yes. Exit codes: success 0, execution 1, input 2, cancellation 130.
        """;

    public static CommandParseResult Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help") return new(null, true, null);
        if (args.Length == 0 || args[0] is not ("diagnose" or "info" or "clip")) return Bad("diagnose, info 또는 clip 명령이 필요합니다.");
        if (args.Length == 2 && args[1] == "--help") return new(null, true, null);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool yes = false;
        for (int i = 1; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--yes" && args[0] == "clip" && !yes) { yes = true; continue; }
            bool allowed = option is "--yt-dlp-path" or "--ffmpeg-path" or "--ffprobe-path" ||
                args[0] is "info" or "clip" && option == "--url" ||
                args[0] == "clip" && option is "--start" or "--end" or "--output" or "--quality" or "--mode";
            if (!allowed || values.ContainsKey(option)) return Bad("알 수 없거나 중복된 옵션입니다.");
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                return Bad("옵션 값이 필요합니다.");
            values.Add(option, args[i]);
        }
        Uri? url = null;
        if (args[0] != "diagnose")
        {
            if (!values.TryGetValue("--url", out string? text) || !Uri.TryCreate(text, UriKind.Absolute, out url)) return Bad("공식 HTTPS 영상 URL이 필요합니다.");
            var parsed = new SupportedVideoSourceParser().Parse(url);
            if (parsed.Status != OperationStatus.Succeeded) return Bad(parsed.Error!.Message);
            url = parsed.Data!.PageUrl;
        }
        var quality = QualityOption.Best;
        var mode = ClipMode.Fast;
        if (args[0] == "clip")
        {
            foreach (string key in new[] { "--start", "--end", "--output" })
                if (!values.ContainsKey(key)) return Bad("clip에는 --start, --end, --output이 필요합니다.");
            foreach (string key in new[] { "--start", "--end" })
                if (!TimecodeInput.TryParse(values[key], out var time, out _) || time.Minutes > 59 || time.Seconds > 59)
                    return Bad("시간은 유효한 HH:MM:SS:FF 형식이어야 합니다.");
            if (values.TryGetValue("--quality", out string? q) && q != "best")
            {
                if (!q.EndsWith('p') || !int.TryParse(q[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
                    !QualityOption.TryCreateResolution(height, out quality, out _)) return Bad("화질은 best 또는 양수 해상도(예:720p)여야 합니다.");
            }
            if (values.TryGetValue("--mode", out string? m))
            {
                if (m is not ("fast" or "accurate")) return Bad("모드는 fast 또는 accurate여야 합니다.");
                mode = m == "fast" ? ClipMode.Fast : ClipMode.Accurate;
            }
        }
        var tools = new Dictionary<string, string>();
        foreach (var (option, name) in new[] { ("--yt-dlp-path", "yt-dlp.exe"), ("--ffmpeg-path", "ffmpeg.exe"), ("--ffprobe-path", "ffprobe.exe") })
            if (values.TryGetValue(option, out var path)) tools.Add(name, path);
        return new(new(args[0], url, values.GetValueOrDefault("--start"), values.GetValueOrDefault("--end"),
            values.GetValueOrDefault("--output"), quality!, mode, yes, tools), false, null);
    }
    private static CommandParseResult Bad(string message) => new(null, false, message);
}
