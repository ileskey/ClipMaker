using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

// Internal ordinary classes keep signed URIs out of automatic record formatting.
internal sealed class HlsSegment(long sequence, Uri uri, PresentationRange playlistRange)
{
    internal long Sequence { get; } = sequence;
    internal Uri Uri { get; } = uri;
    internal PresentationRange PlaylistRange { get; } = playlistRange;
}

internal sealed class HlsMediaPlaylist(Uri uri, Uri initialization, IReadOnlyList<HlsSegment> segments, SignedMediaTime duration)
{
    internal Guid Id { get; } = Guid.NewGuid();
    internal Uri Uri { get; } = uri;
    internal Uri Initialization { get; } = initialization;
    internal IReadOnlyList<HlsSegment> Segments { get; } = segments;
    internal SignedMediaTime Duration { get; } = duration;
}

internal sealed class HlsMediaPlaylistParser
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private const int maximumSegments = 20000;

    internal OperationResult<HlsMediaPlaylist> Parse(ReadOnlyMemory<byte> bytes, Uri playlistUri)
    {
        ArgumentNullException.ThrowIfNull(playlistUri);
        try
        {
            _ = new SourceHttpRequest(playlistUri, SourceHttpMode.Resource, 1);
            if (bytes.Length > MaximumBytes) return Fail(OperationErrorCode.InvalidMetadata);
            string text = new UTF8Encoding(false, true).GetString(bytes.Span);
            string[] lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            if (lines.Length == 0 || lines[0] != "#EXTM3U" || lines.Any(line => line.Length > 65536))
                return Fail(OperationErrorCode.InvalidMetadata);
            var segments = new List<HlsSegment>();
            SignedMediaTime position = SignedMediaTime.Zero;
            SignedMediaTime? pending = null;
            Uri? map = null;
            long sequence = 0;
            long? target = null;
            bool sequenceSeen = false, end = false, versionSeen = false, discontinuitySequenceSeen = false;
            foreach (string line in lines.Skip(1))
            {
                if (line.Length == 0) continue;
                if (end && line.StartsWith("#EXT", StringComparison.Ordinal)) return Fail(OperationErrorCode.InvalidMetadata);
                if (!line.StartsWith('#'))
                {
                    if (end || pending is null || target is null || segments.Count >= maximumSegments)
                        return Fail(OperationErrorCode.InvalidMetadata);
                    if (map is null) return Fail(OperationErrorCode.UnsupportedFormat);
                    if (!ResolveUri(playlistUri, line, out var uri)) return Fail(OperationErrorCode.InvalidMetadata);
                    if (sequence == long.MaxValue || pending.CompareTo(Time(target.Value * (BigInteger)2 + 1, 2)) >= 0)
                        return Fail(OperationErrorCode.InvalidMetadata);
                    var finish = position + pending;
                    PresentationRange.TryCreate(position, finish, out var range, out _);
                    segments.Add(new(sequence++, uri!, range!));
                    position = finish;
                    pending = null;
                }
                else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                {
                    int comma = line.IndexOf(',', 8);
                    if (pending is not null || comma < 0 || !Duration(line[8..comma], out pending)) return Fail(OperationErrorCode.InvalidMetadata);
                }
                else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
                {
                    if (map is not null || segments.Count != 0 || pending is not null) return Fail(OperationErrorCode.UnsupportedFormat);
                    var match = Regex.Match(line[11..], "^URI=\"([^\"\r\n]+)\"$", RegexOptions.CultureInvariant);
                    if (!match.Success) return Fail(OperationErrorCode.UnsupportedFormat);
                    if (!ResolveUri(playlistUri, match.Groups[1].Value, out map)) return Fail(OperationErrorCode.InvalidMetadata);
                }
                else if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
                {
                    if (target is not null || segments.Count != 0 || !Integer(line[22..], out var value) || value == 0)
                        return Fail(OperationErrorCode.InvalidMetadata);
                    target = value;
                }
                else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
                {
                    if (sequenceSeen || segments.Count != 0 || !Integer(line[22..], out sequence)) return Fail(OperationErrorCode.InvalidMetadata);
                    sequenceSeen = true;
                }
                else if (line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE:", StringComparison.Ordinal))
                {
                    if (discontinuitySequenceSeen || segments.Count != 0 || !Integer(line[30..], out var value)) return Fail(OperationErrorCode.InvalidMetadata);
                    if (value != 0) return Fail(OperationErrorCode.UnsupportedFormat);
                    discontinuitySequenceSeen = true;
                }
                else if (line.StartsWith("#EXT-X-VERSION:", StringComparison.Ordinal))
                {
                    if (versionSeen || !Integer(line[15..], out var version) || version < 6 || version > 7)
                        return Fail(OperationErrorCode.UnsupportedFormat);
                    versionSeen = true;
                }
                else if (line == "#EXT-X-ENDLIST") { if (pending is not null) return Fail(OperationErrorCode.InvalidMetadata); end = true; }
                else if (line == "#EXT-X-INDEPENDENT-SEGMENTS" || line == "#EXT-X-PLAYLIST-TYPE:VOD" || line == "#EXT-X-KEY:METHOD=NONE") { }
                else if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:", StringComparison.Ordinal) || line.StartsWith("#EXT-X-DATERANGE:", StringComparison.Ordinal)) { }
                else if (line.StartsWith("#EXT", StringComparison.Ordinal)) return Fail(OperationErrorCode.UnsupportedFormat);
                // Other comment lines carry no media or clock authority.
            }
            if (!end || !versionSeen || map is null || target is null || segments.Count == 0 || pending is not null)
                return Fail(OperationErrorCode.UnsupportedFormat);
            return OperationResult<HlsMediaPlaylist>.Succeeded(new(playlistUri, map, Array.AsReadOnly(segments.ToArray()), position));
        }
        catch (DecoderFallbackException) { return Fail(OperationErrorCode.InvalidMetadata); }
        catch (ArgumentException) { return Fail(OperationErrorCode.InvalidMetadata); }
    }

    private static bool ResolveUri(Uri parent, string text, out Uri? uri)
    {
        uri = null;
        if (text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || !Uri.TryCreate(parent, text, out uri)) return false;
        _ = new SourceHttpRequest(uri, SourceHttpMode.Resource, 1);
        return true;
    }
    private static bool Integer(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && text.Length > 0 && text.All(char.IsAsciiDigit);
    private static bool Duration(string text, out SignedMediaTime? value)
    {
        value = null;
        if (text.Length is 0 or > 32 || text.Any(character => !char.IsAsciiDigit(character) && character != '.')) return false;
        var parts = text.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Length == 0)) return false;
        var numerator = BigInteger.Parse(string.Concat(parts), NumberStyles.None, CultureInfo.InvariantCulture);
        if (numerator <= 0) return false;
        return SignedMediaTime.TryCreate(numerator, BigInteger.Pow(10, parts.Length == 2 ? parts[1].Length : 0), out value, out _);
    }
    private static SignedMediaTime Time(BigInteger n, BigInteger d)
    {
        SignedMediaTime.TryCreate(n, d, out var value, out _);
        return value!;
    }
    private static OperationResult<HlsMediaPlaylist> Fail(OperationErrorCode code) =>
        OperationResult<HlsMediaPlaylist>.Failed(new(code, OperationErrorCategory.Execution, "선택 HLS media playlist의 지원 조건/완전성을 확인하지 못했습니다."));
}
