using System.Text;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

// A provider declaration, not a verified relation between independently selected movie clocks.
internal sealed class HlsPresentationGroup(string groupId, Uri video, Uri audio)
{
    internal string GroupId { get; } = groupId;
    internal Uri Video { get; } = video;
    internal Uri Audio { get; } = audio;
}

internal sealed class HlsPresentationGroupReader
{
    internal OperationResult<HlsPresentationGroup> Read(ReadOnlyMemory<byte> bytes, Uri master,
        string videoRepresentation, string audioRepresentation)
    {
        try
        {
            if (bytes.Length is < 1 or > 512 * 1024) throw new InvalidDataException();
            _ = new SourceHttpRequest(master, SourceHttpMode.Resource, 1);
            string text = new UTF8Encoding(false, true).GetString(bytes.Span);
            var lines = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length != 0).ToArray();
            if (lines.Length is < 1 or > 4096 || lines[0] != "#EXTM3U" || lines.Any(s => s.Length > 16384)) throw new InvalidDataException();
            var videos = new List<(string Group, Uri Uri)>();
            var audios = new List<(string Group, Uri Uri)>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                {
                    var attrs = Attributes(lines[i][18..]);
                    if (++i == lines.Length || lines[i].StartsWith('#')) throw new InvalidDataException();
                    var uri = Resolve(master, lines[i]);
                    if (Representation(uri) == videoRepresentation)
                    {
                        if (!attrs.TryGetValue("AUDIO", out var group) || string.IsNullOrWhiteSpace(group)) throw new InvalidDataException();
                        videos.Add((group, uri));
                    }
                }
                else if (lines[i].StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
                {
                    var attrs = Attributes(lines[i][13..]);
                    if (attrs.GetValueOrDefault("TYPE") != "AUDIO") continue;
                    if (!attrs.TryGetValue("URI", out var path)) continue; // In-band audio is outside this split-reference strategy.
                    var uri = Resolve(master, path);
                    if (Representation(uri) == audioRepresentation)
                    {
                        if (!attrs.TryGetValue("GROUP-ID", out var group) || string.IsNullOrWhiteSpace(group)) throw new InvalidDataException();
                        audios.Add((group, uri));
                    }
                }
                else if (!lines[i].StartsWith('#')) throw new InvalidDataException();
            }
            if (videos.Count != 1 || audios.Count != 1 || videos[0].Group != audios[0].Group) throw new InvalidDataException();
            return OperationResult<HlsPresentationGroup>.Succeeded(new(videos[0].Group, videos[0].Uri, audios[0].Uri));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or DecoderFallbackException)
        { return OperationResult<HlsPresentationGroup>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "선택 참조의 단일 provider presentation group을 확인하지 못했습니다.", "ProviderPresentationGroup")); }
    }

    private static Uri Resolve(Uri master, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) throw new InvalidDataException();
        var uri = new Uri(master, path);
        _ = new SourceHttpRequest(uri, SourceHttpMode.Resource, 1);
        return uri;
    }

    private static string? Representation(Uri uri)
    {
        var candidates = new List<string>();
        var parts = uri.AbsolutePath.Split('/');
        for (int i = 0; i + 1 < parts.Length; i++) if (parts[i] == "itag") candidates.Add(Uri.UnescapeDataString(parts[i + 1]));
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == "itag" && pair.Length == 2) candidates.Add(Uri.UnescapeDataString(pair[1]));
        }
        if (candidates.Count > 1) throw new InvalidDataException();
        return candidates.SingleOrDefault();
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        int at = 0;
        while (at < text.Length)
        {
            int equal = text.IndexOf('=', at);
            if (equal <= at) throw new InvalidDataException();
            string key = text[at..equal];
            if (key.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))) throw new InvalidDataException();
            at = equal + 1;
            int end;
            string value;
            if (at < text.Length && text[at] == '"')
            {
                end = text.IndexOf('"', ++at);
                if (end < 0) throw new InvalidDataException();
                value = text[at..end]; at = end + 1;
            }
            else
            {
                end = text.IndexOf(',', at); if (end < 0) end = text.Length;
                value = text[at..end]; at = end;
            }
            if (value.Length == 0 || value.Any(char.IsControl) || !result.TryAdd(key, value)) throw new InvalidDataException();
            if (at < text.Length && (text[at++] != ',' || at == text.Length)) throw new InvalidDataException();
        }
        return result;
    }
}
