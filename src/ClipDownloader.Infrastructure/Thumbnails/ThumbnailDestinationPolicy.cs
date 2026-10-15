using System.Net;
using System.Net.Sockets;

namespace ClipDownloader.Infrastructure.Thumbnails;

internal static class ThumbnailDestinationPolicy
{
    // Conservative special-use blocks, including globally routed protocol exceptions.
    // IANA special-purpose registries, reviewed 2026-10-15. No NAT64/6to4/Teredo tunnelling.
    private static readonly IPNetwork[] DeniedV4 = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16",
        "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16",
        "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/3"
    }.Select(IPNetwork.Parse).ToArray();
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork[] DeniedV6 = new[]
    { "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20" }.Select(IPNetwork.Parse).ToArray();

    internal static bool Allows(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || !uri.IsWellFormedOriginalString()) return false;
        string text = uri.OriginalString;
        if (text.Any(char.IsWhiteSpace) || text.Contains('\\') || text.Contains('@') ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.HostNameType != UriHostNameType.Dns ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        string host = uri.IdnHost.TrimEnd('.');
        return host.Contains('.') && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
            !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return !DeniedV4.Any(n => n.Contains(ip));
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId == 0 &&
            GlobalV6.Contains(ip) && !DeniedV6.Any(n => n.Contains(ip));
    }
}
