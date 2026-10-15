using System.Net;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Thumbnails;

public sealed class HttpThumbnailContentService : IThumbnailContentService, IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan deadline;
    public HttpThumbnailContentService() : this(CreateHandler(ThumbnailConnection.Production()), TimeSpan.FromSeconds(10)) { }
    internal HttpThumbnailContentService(HttpMessageHandler handler, TimeSpan? deadline = null)
    {
        client = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        this.deadline = deadline ?? TimeSpan.FromSeconds(10);
    }
    internal static SocketsHttpHandler CreateHandler(ThumbnailConnection connection) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, Credentials = null,
        AutomaticDecompression = DecompressionMethods.None,
        // No pool reuse across metadata reloads; every acquisition revalidates its destination.
        PooledConnectionLifetime = TimeSpan.Zero,
        ConnectCallback = (context, token) => connection.OpenAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, token)
    };

    public async Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return ThumbnailContentResult.Cancelled();
        if (!ThumbnailDestinationPolicy.Allows(uri)) return ThumbnailContentResult.Failed(ThumbnailFailure.UnsafeUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        var token = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri)
            { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Accept.ParseAdd("image/jpeg, image/png");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400) return ThumbnailContentResult.Failed(ThumbnailFailure.RedirectRejected);
            if (status != 200) return ThumbnailContentResult.Failed(ThumbnailFailure.HttpFailure);
            var content = response.Content;
            if (content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                return ThumbnailContentResult.Failed(ThumbnailFailure.UnsupportedImage);
            string? mime = content.Headers.ContentType?.MediaType;
            ThumbnailImageKind? kind = mime?.ToLowerInvariant() switch
            { "image/jpeg" => ThumbnailImageKind.Jpeg, "image/png" => ThumbnailImageKind.Png, _ => null };
            if (kind is null) return ThumbnailContentResult.Failed(ThumbnailFailure.UnsupportedImage);
            if (content.Headers.ContentLength > ThumbnailContentResult.MaximumBytes)
                return ThumbnailContentResult.Failed(ThumbnailFailure.TooLarge);
            using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length,
                    ThumbnailContentResult.MaximumBytes + 1L - buffer.Length)), token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > ThumbnailContentResult.MaximumBytes) return ThumbnailContentResult.Failed(ThumbnailFailure.TooLarge);
                buffer.Write(chunk, 0, read);
            }
            token.ThrowIfCancellationRequested();
            if (buffer.Length == 0 || content.Headers.ContentLength is { } declared && declared != buffer.Length)
                return ThumbnailContentResult.Failed(ThumbnailFailure.InvalidImage);
            var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            bool valid = kind == ThumbnailImageKind.Png ? bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) :
                bytes.StartsWith(new byte[] { 255, 216, 255 });
            return valid ? ThumbnailContentResult.Success(bytes, kind.Value) : ThumbnailContentResult.Failed(ThumbnailFailure.InvalidImage);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        { return cancellationToken.IsCancellationRequested ? ThumbnailContentResult.Cancelled() : ThumbnailContentResult.Failed(ThumbnailFailure.TimedOut); }
        catch (Exception e) when (e is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            bool rejected = false;
            for (Exception? cause = e; cause is not null; cause = cause.InnerException)
                if (cause is ThumbnailDestinationException) rejected = true;
            return ThumbnailContentResult.Failed(rejected ? ThumbnailFailure.DestinationRejected : ThumbnailFailure.HttpFailure);
        }
    }
    public void Dispose() => client.Dispose();
}
