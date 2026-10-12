using System.Collections.ObjectModel;

namespace ClipDownloader.Infrastructure.Downloads;

internal enum SourceHttpMode { Resource, HeaderRange, QueryRange }

// Ephemeral Infrastructure-only locator. Never put this request in public job data or logs.
internal sealed class SourceHttpRequest
{
    internal const int MaximumReadBytes = 32 * 1024 * 1024;
    private static readonly HashSet<string> allowedHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "User-Agent", "Accept", "Accept-Language", "Referer", "Origin", "Sec-Fetch-Mode" };

    internal static bool IsSupportedHeader(string name) => allowedHeaders.Contains(name);

    internal Uri Uri { get; }
    internal SourceHttpMode Mode { get; }
    internal int MaximumBytes { get; }
    internal long? Start { get; }
    internal int? Count { get; }
    internal long? End => Start is { } start && Count is { } count ? start + count - 1 : null;
    internal IReadOnlyDictionary<string, string> Headers { get; }

    internal SourceHttpRequest(Uri uri, SourceHttpMode mode, int maximumBytes, long? start = null, int? count = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != System.Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("An HTTPS source locator without credentials or fragment is required.", nameof(uri));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (maximumBytes <= 0 || maximumBytes > MaximumReadBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (mode == SourceHttpMode.Resource)
        {
            if (start is not null || count is not null) throw new ArgumentException("Resource reads cannot carry a byte range.");
        }
        else if (start is null or < 0 || count is null or <= 0 || count > maximumBytes || start > long.MaxValue - (count.Value - 1))
            throw new ArgumentException("A bounded, representable byte range is required.");
        if (mode != SourceHttpMode.Resource && uri.Query.TrimStart('?').Split('&').Any(part =>
            string.Equals(System.Uri.UnescapeDataString(part.Split('=')[0]), "range", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The source locator already contains a range query.", nameof(uri));

        var copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers ?? [])
        {
            if (!allowedHeaders.Contains(header.Key) || header.Value is null || header.Value.Contains('\r') || header.Value.Contains('\n') ||
                !copiedHeaders.TryAdd(header.Key, header.Value))
                throw new ArgumentException("Unsupported or invalid source request header.", nameof(headers));
        }
        Uri = uri;
        Mode = mode;
        MaximumBytes = maximumBytes;
        Start = start;
        Count = count;
        Headers = new ReadOnlyDictionary<string, string>(copiedHeaders);
    }

    internal Uri RequestUri => Mode != SourceHttpMode.QueryRange ? Uri :
        new(Uri.AbsoluteUri + (Uri.Query.Length == 0 ? "?" : "&") +
            FormattableString.Invariant($"range={Start}-{End}"));
}
