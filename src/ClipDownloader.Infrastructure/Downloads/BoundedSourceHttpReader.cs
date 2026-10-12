using System.Net;
using System.Net.Http.Headers;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal enum SourceHttpFailureKind { UnexpectedResponse, SizeLimitExceeded, TransportFailed }

internal sealed class SourceHttpReadResult
{
    internal OperationStatus Status { get; }
    internal byte[]? Bytes { get; }
    internal SourceHttpFailureKind? Failure { get; }
    internal int? HttpStatus { get; }
    internal long? ResourceLength { get; }
    internal string? Message => Failure switch
    {
        SourceHttpFailureKind.UnexpectedResponse => "source 응답의 범위 또는 body 길이가 요청과 일치하지 않습니다.",
        SourceHttpFailureKind.SizeLimitExceeded => "source 응답이 byte 검사 상한을 초과했습니다.",
        SourceHttpFailureKind.TransportFailed => "source 응답을 완전히 수신하지 못했습니다.",
        _ => null
    };

    private SourceHttpReadResult(OperationStatus status, byte[]? bytes, SourceHttpFailureKind? failure, int? httpStatus, long? resourceLength)
    {
        Status = status; Bytes = bytes; Failure = failure; HttpStatus = httpStatus; ResourceLength = resourceLength;
    }
    internal static SourceHttpReadResult Success(byte[] bytes, int status, long? length) => new(OperationStatus.Succeeded, bytes, null, status, length);
    internal static SourceHttpReadResult Failed(SourceHttpFailureKind failure, int? status) => new(OperationStatus.Failed, null, failure, status, null);
    internal static SourceHttpReadResult Cancelled() => new(OperationStatus.Cancelled, null, null, null, null);
}

/// <summary>One bounded request, no redirects, cookies, automatic retry or full-resource fallback.</summary>
internal sealed class BoundedSourceHttpReader : IDisposable
{
    private readonly HttpClient client;

    // An injected handler is a trusted test/composition dependency, not source input.
    internal BoundedSourceHttpReader(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None
        }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<SourceHttpReadResult> ReadAsync(SourceHttpRequest source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        int? status = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, source.RequestUri);
            request.Headers.AcceptEncoding.Add(new("identity"));
            if (source.Mode == SourceHttpMode.HeaderRange) request.Headers.Range = new(source.Start, source.End);
            foreach (var header in source.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            status = (int)response.StatusCode;
            var content = response.Content.Headers;
            if (content.Contains("Content-Length") && content.ContentLength is null)
                return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);
            if (content.ContentEncoding.Any(encoding => !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
                return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);

            long? expected;
            long? resourceLength = null;
            if (source.Mode == SourceHttpMode.Resource)
            {
                if (response.StatusCode != HttpStatusCode.OK || content.Contains("Content-Range"))
                    return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);
                expected = content.ContentLength;
            }
            else
            {
                bool exactRange = response.StatusCode == HttpStatusCode.PartialContent && content.ContentRange is { } range &&
                    range.Unit == "bytes" && range.From == source.Start && range.To == source.End && range.Length > source.End;
                bool exactQuery = source.Mode == SourceHttpMode.QueryRange && response.StatusCode == HttpStatusCode.OK &&
                    !content.Contains("Content-Range") && content.ContentLength == source.Count;
                if ((!exactRange && !exactQuery) || content.ContentLength is { } length && length != source.Count)
                    return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);
                expected = source.Count;
                resourceLength = exactRange ? content.ContentRange!.Length : null;
            }
            if (expected > source.MaximumBytes)
                return SourceHttpReadResult.Failed(SourceHttpFailureKind.SizeLimitExceeded, status);

            using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, source.MaximumBytes - (int)output.Length + 1)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > source.MaximumBytes)
                    return SourceHttpReadResult.Failed(SourceHttpFailureKind.SizeLimitExceeded, status);
                if (expected is { } bytes && output.Length + read > bytes)
                    return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);
                output.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (expected is { } count && output.Length != count)
                return SourceHttpReadResult.Failed(SourceHttpFailureKind.UnexpectedResponse, status);
            return SourceHttpReadResult.Success(output.ToArray(), status.Value,
                source.Mode == SourceHttpMode.Resource ? content.ContentLength : resourceLength);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return SourceHttpReadResult.Cancelled(); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        { return SourceHttpReadResult.Failed(SourceHttpFailureKind.TransportFailed, status); }
    }

    public void Dispose() => client.Dispose();
}
