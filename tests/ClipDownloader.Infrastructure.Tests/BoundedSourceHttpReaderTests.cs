using System.Net;
using System.Net.Http.Headers;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class BoundedSourceHttpReaderTests
{
    private static readonly Uri locator = new("https://media.example/asset?token=private-test-value");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private sealed class Body(byte[] bytes, CancellationTokenSource? cancel = null, bool cancelAtEof = false,
        bool fail = false) : Stream
    {
        private int position;
        internal int ReadCalls { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            ReadCalls++;
            if (fail) throw new IOException("private-test-value: transport detail must not escape");
            if (cancel is not null && (cancelAtEof ? position == bytes.Length : position > 0)) cancel.Cancel();
            token.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, Math.Min(bytes.Length - position, 3));
            bytes.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, Body body, long? length = null,
        long? begin = null, long? end = null, long total = 100)
    {
        var response = new HttpResponseMessage(status) { Content = new StreamContent(body) };
        if (length is not null) response.Content.Headers.ContentLength = length;
        if (begin is not null) response.Content.Headers.ContentRange = new(begin.Value, end!.Value, total);
        return response;
    }

    [Fact]
    public async Task ExactHeaderRangeRequires206AndReturnsOriginalResourceLength()
    {
        var body = new Body([1, 2, 3, 4]);
        var handler = new Handler(request =>
        {
            Assert.Equal("bytes=10-13", request.Headers.Range!.ToString());
            Assert.Equal(locator, request.RequestUri);
            Assert.Equal("identity", Assert.Single(request.Headers.AcceptEncoding).Value);
            return Response(HttpStatusCode.PartialContent, body, 4, 10, 13, 1000);
        });
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.HeaderRange, 8, 10, 4), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Bytes);
        Assert.Equal(1000, result.ResourceLength);
        Assert.Equal(1, handler.Calls);
        Assert.True(body.Disposed);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(206)]
    public async Task QueryRangeIsExplicitAndNeverAlsoSendsRangeHeader(int status)
    {
        var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Range);
            Assert.Equal(locator.AbsoluteUri + "&range=10-13", request.RequestUri!.AbsoluteUri);
            return Response((HttpStatusCode)status, new([1, 2, 3, 4]), 4,
                status == 206 ? 10 : null, status == 206 ? 13 : null, 1000);
        });
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.QueryRange, 8, 10, 4), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(status == 206 ? 1000L : null, result.ResourceLength);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(416)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task HeaderRangeRejectsFullRedirectAndErrorResponsesWithoutRetryOrBodyRead(int status)
    {
        var body = new Body([1, 2, 3, 4]);
        var handler = new Handler(_ => Response((HttpStatusCode)status, body, 4));
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.HeaderRange, 8, 10, 4), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(SourceHttpFailureKind.UnexpectedResponse, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(status, result.HttpStatus);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, body.ReadCalls);
        Assert.True(body.Disposed);
        Assert.DoesNotContain("private-test-value", result.Message!);
    }

    [Theory]
    [InlineData(9, 12)]
    [InlineData(10, 14)]
    public async Task RejectsMismatchedContentRange(long begin, long end)
    {
        using var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.PartialContent,
            new([1, 2, 3, 4]), null, begin, end)));
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.HeaderRange, 8, 10, 4), TestContext.Current.CancellationToken);
        Assert.Equal(SourceHttpFailureKind.UnexpectedResponse, result.Failure);
        Assert.Null(result.Bytes);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task HeaderRangeRejectsShortAndSurplusBodiesEvenWithoutContentLength(int size)
    {
        using var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.PartialContent,
            new(new byte[size]), null, 10, 13)));
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.HeaderRange, 8, 10, 4), TestContext.Current.CancellationToken);
        Assert.Equal(SourceHttpFailureKind.UnexpectedResponse, result.Failure);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task Query200NeedsExactDeclaredLengthAndIdentityEncoding()
    {
        foreach (bool encoded in new[] { false, true })
        {
            var body = new Body([1, 2, 3, 4]);
            using var reader = new BoundedSourceHttpReader(new Handler(_ =>
            {
                var response = Response(HttpStatusCode.OK, body, encoded ? 4 : null);
                if (encoded) response.Content.Headers.ContentEncoding.Add("gzip");
                return response;
            }));
            var result = await reader.ReadAsync(new(locator, SourceHttpMode.QueryRange, 8, 10, 4), TestContext.Current.CancellationToken);
            Assert.Equal(SourceHttpFailureKind.UnexpectedResponse, result.Failure);
            Assert.Equal(0, body.ReadCalls);
        }
    }

    [Fact]
    public async Task ResourceGetIsBoundedWithOrWithoutDeclaredLength()
    {
        using (var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.OK, new([1, 2, 3]), null))))
        {
            var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 3), TestContext.Current.CancellationToken);
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(new byte[] { 1, 2, 3 }, result.Bytes);
        }
        var oversized = new Body(new byte[10]);
        using (var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.OK, oversized, 10))))
        {
            var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 3), TestContext.Current.CancellationToken);
            Assert.Equal(SourceHttpFailureKind.SizeLimitExceeded, result.Failure);
            Assert.Equal(0, oversized.ReadCalls);
        }
        using (var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.OK, new([1, 2, 3, 4]), null))))
        {
            var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 3), TestContext.Current.CancellationToken);
            Assert.Equal(SourceHttpFailureKind.SizeLimitExceeded, result.Failure);
            Assert.Null(result.Bytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringBodyOrAtEofNeverReturnsPartialSuccess(bool atEof)
    {
        using var stop = new CancellationTokenSource();
        var body = new Body([1, 2, 3, 4], stop, atEof);
        using var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.OK, body, 4)));
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 8), stop.Token);
        Assert.Equal(OperationStatus.Cancelled, result.Status);
        Assert.Null(result.Bytes);
        Assert.Null(result.Failure);
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotSendAndTransportErrorDoesNotLeakDetails()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var handler = new Handler(_ => Response(HttpStatusCode.OK, new([1])));
        using (var reader = new BoundedSourceHttpReader(handler))
        {
            Assert.Equal(OperationStatus.Cancelled, (await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 8), stop.Token)).Status);
            Assert.Equal(0, handler.Calls);
        }
        using (var reader = new BoundedSourceHttpReader(new Handler(_ => Response(HttpStatusCode.OK, new([], fail: true)))))
        {
            var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 8), TestContext.Current.CancellationToken);
            Assert.Equal(SourceHttpFailureKind.TransportFailed, result.Failure);
            Assert.Null(result.Bytes);
            Assert.DoesNotContain("private-test-value", result.Message!);
        }
    }

    [Theory]
    [InlineData("Content-Length")]
    [InlineData("Content-Range")]
    public async Task MalformedHeadersCannotMasqueradeAsAbsentHeaders(string header)
    {
        var body = new Body([1, 2, 3]);
        using var reader = new BoundedSourceHttpReader(new Handler(_ =>
        {
            var response = Response(HttpStatusCode.OK, body);
            Assert.True(response.Content.Headers.TryAddWithoutValidation(header, "not-a-valid-header-value"));
            return response;
        }));
        var result = await reader.ReadAsync(new(locator, SourceHttpMode.Resource, 8), TestContext.Current.CancellationToken);
        Assert.Equal(SourceHttpFailureKind.UnexpectedResponse, result.Failure);
        Assert.Null(result.Bytes);
        Assert.Equal(0, body.ReadCalls);
    }

    [Fact]
    public void RequestValidationProtectsRangesHeadersAndQueryProvenance()
    {
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(new("http://media.example/asset"), SourceHttpMode.Resource, 8));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(new("https://user:pass@media.example/asset"), SourceHttpMode.Resource, 8));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(new("https://media.example/asset#fragment"), SourceHttpMode.Resource, 8));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(locator, SourceHttpMode.HeaderRange, 8, long.MaxValue, 2));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(locator, SourceHttpMode.HeaderRange, 8, 0, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceHttpRequest(locator, SourceHttpMode.Resource, SourceHttpRequest.MaximumReadBytes + 1));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(new("https://media.example/asset?%72ange=1-2"), SourceHttpMode.QueryRange, 8, 0, 4));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(new("https://media.example/asset?range=1-2"), SourceHttpMode.HeaderRange, 8, 0, 4));
        foreach (string name in new[] { "Range", "Host", "Cookie", "Authorization", "Accept-Encoding" })
            Assert.Throws<ArgumentException>(() => new SourceHttpRequest(locator, SourceHttpMode.Resource, 8, headers: [new(name, "private-test-value")]));
        Assert.Throws<ArgumentException>(() => new SourceHttpRequest(locator, SourceHttpMode.Resource, 8, headers: [new("User-Agent", "value\r\ninjected")]));
        var headers = new Dictionary<string, string> { ["User-Agent"] = "original" };
        var request = new SourceHttpRequest(locator, SourceHttpMode.Resource, 8, headers: headers);
        headers["User-Agent"] = "changed";
        Assert.Equal("original", request.Headers["user-agent"]);
    }
}
