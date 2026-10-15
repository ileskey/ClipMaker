using System.Net;
using System.Net.Sockets;

namespace ClipDownloader.Infrastructure.Thumbnails;

internal sealed class ThumbnailDestinationException : IOException;

internal sealed class ThumbnailConnection(
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect)
{
    internal static ThumbnailConnection Production() => new(
        (host, token) => Dns.GetHostAddressesAsync(host, token), ConnectSocketAsync);

    internal async ValueTask<Stream> OpenAsync(string host, int port, CancellationToken token)
    {
        if (port != 443 || !ThumbnailDestinationPolicy.Allows(new UriBuilder("https", host, port).Uri))
            throw new ThumbnailDestinationException();
        var addresses = await resolve(host, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (addresses.Length == 0 || addresses.Any(ip => !ThumbnailDestinationPolicy.IsPublic(ip)))
            throw new ThumbnailDestinationException();
        // Exactly one connection attempt, using the already validated address. TLS still uses the URI host.
        return await connect(addresses[0], port, token).ConfigureAwait(false);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(IPAddress ip, int port, CancellationToken token)
    {
        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(ip, port), token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
