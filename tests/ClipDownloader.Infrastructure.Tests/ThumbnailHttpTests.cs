using System.Net;
using System.Net.Http.Headers;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Thumbnails;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ThumbnailHttpTests
{
    private static readonly Uri ImageUri = new("https://images.example.com/image.png");
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Theory]
    [InlineData("http://images.example.com/a")]
    [InlineData("https://images.example.com:444/a")]
    [InlineData("https://localhost/a")]
    [InlineData("https://a.local/a")]
    [InlineData("https://a.localhost./a")]
    [InlineData("https://single/a")]
    [InlineData("https://127.0.0.1/a")]
    [InlineData("https://[::1]/a")]
    [InlineData("https://images.example.com/a?q=sentinel")]
    [InlineData("https://images.example.com/a#sentinel")]
    [InlineData("https://user@images.example.com/a")]
    [InlineData("https://images.example.com/@sentinel")]
    [InlineData("https://images.example.com/a b")]
    [InlineData("https://images.example.com/a\\b")]
    public async Task UnsafeUriNeverSends(string text)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("must not send"));
        using var service = new HttpThumbnailContentService(handler);
        Assert.Equal(ThumbnailFailure.UnsafeUri, (await service.GetAsync(new Uri(text), TestContext.Current.CancellationToken)).Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("0.1.2.3", false)] [InlineData("9.255.255.255", true)]
    [InlineData("10.0.0.0", false)] [InlineData("10.255.255.255", false)] [InlineData("11.0.0.0", true)]
    [InlineData("100.63.255.255", true)] [InlineData("100.64.0.0", false)] [InlineData("100.127.255.255", false)] [InlineData("100.128.0.0", true)]
    [InlineData("127.9.8.7", false)] [InlineData("169.254.0.1", false)]
    [InlineData("172.15.255.255", true)] [InlineData("172.16.0.0", false)] [InlineData("172.31.255.255", false)] [InlineData("172.32.0.0", true)]
    [InlineData("192.0.0.9", false)] [InlineData("192.0.2.1", false)] [InlineData("192.88.99.1", false)] [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.0", false)] [InlineData("198.19.255.255", false)] [InlineData("198.20.0.0", true)]
    [InlineData("198.51.100.2", false)] [InlineData("203.0.113.1", false)]
    [InlineData("223.255.255.255", true)] [InlineData("224.0.0.0", false)] [InlineData("255.255.255.255", false)]
    [InlineData("::", false)] [InlineData("::1", false)] [InlineData("::ffff:10.0.0.1", false)] [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::808:808", false)] [InlineData("fc00::1", false)] [InlineData("fe80::1", false)] [InlineData("ff02::1", false)]
    [InlineData("2001::1", false)] [InlineData("2001:1ff::1", false)] [InlineData("2001:200::1", true)]
    [InlineData("2001:db8::1", false)] [InlineData("2002::1", false)] [InlineData("3fff::1", false)]
    [InlineData("2606:4700::1111", true)] [InlineData("4000::1", false)]
    public void AddressClassification(string address, bool allowed) => Assert.Equal(allowed, ThumbnailDestinationPolicy.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task MixedDnsRejectsAllWithoutConnectingAndApprovedIpIsPinned()
    {
        int calls = 0;
        var connection = new ThumbnailConnection((_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }),
            (_, _, _) => { calls++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        await Assert.ThrowsAsync<ThumbnailDestinationException>(async () => await connection.OpenAsync("images.example.com", 443, TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
        IPAddress? connected = null; int resolves = 0;
        connection = new((_, _) => { resolves++; return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }); },
            (ip, port, _) => { connected = ip; Assert.Equal(443, port); return ValueTask.FromResult<Stream>(new MemoryStream()); });
        using var stream = await connection.OpenAsync("images.example.com", 443, TestContext.Current.CancellationToken);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), connected); Assert.Equal(1, resolves);
    }

    [Theory]
    [InlineData(200, null)] [InlineData(301, ThumbnailFailure.RedirectRejected)] [InlineData(302, ThumbnailFailure.RedirectRejected)]
    [InlineData(304, ThumbnailFailure.RedirectRejected)] [InlineData(404, ThumbnailFailure.HttpFailure)] [InlineData(500, ThumbnailFailure.HttpFailure)]
    public async Task StatusRequestAndOwnership(int status, ThumbnailFailure? failure)
    {
        var stream = new TrackedStream(Signature);
        using var content = new StreamContent(stream); content.Headers.ContentType = new("image/png");
        using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(HttpVersion.Version11, request.Version); Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
            Assert.Null(request.Headers.Authorization); Assert.Null(request.Headers.Referrer); Assert.Null(request.Headers.Host);
            return Task.FromResult(response);
        });
        using var service = new HttpThumbnailContentService(handler);
        var result = await service.GetAsync(ImageUri, TestContext.Current.CancellationToken);
        Assert.Equal(failure, result.Failure); Assert.True(stream.Disposed); Assert.Equal(1, handler.Calls);
        if (failure is null) Assert.Equal(Signature, result.Content.ToArray());
    }

    [Theory]
    [InlineData("image/webp", null, ThumbnailFailure.UnsupportedImage)]
    [InlineData("text/html", null, ThumbnailFailure.UnsupportedImage)]
    [InlineData("image/png", "gzip", ThumbnailFailure.UnsupportedImage)]
    [InlineData("image/jpeg", null, ThumbnailFailure.InvalidImage)]
    public async Task MimeEncodingAndSignature(string mime, string? encoding, ThumbnailFailure expected)
    {
        using var handler = new Handler((_, _) =>
        {
            var content = new ByteArrayContent(Signature); content.Headers.ContentType = new(mime);
            if (encoding is not null) content.Headers.ContentEncoding.Add(encoding);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var service = new HttpThumbnailContentService(handler);
        Assert.Equal(expected, (await service.GetAsync(ImageUri, TestContext.Current.CancellationToken)).Failure);
    }

    [Theory]
    [InlineData(0, null, ThumbnailFailure.InvalidImage)]
    [InlineData(8, 7L, ThumbnailFailure.InvalidImage)]
    [InlineData(8, 9L, ThumbnailFailure.InvalidImage)]
    [InlineData(8, 4194305L, ThumbnailFailure.TooLarge)]
    [InlineData(4194304, null, null)]
    [InlineData(4194305, null, ThumbnailFailure.TooLarge)]
    [InlineData(4194305, 8L, ThumbnailFailure.TooLarge)]
    public async Task BoundedStreamingEvenWhenLengthLies(int length, long? declared, ThumbnailFailure? failure)
    {
        byte[] bytes = new byte[length]; Signature.AsSpan(0, Math.Min(length, 8)).CopyTo(bytes);
        var stream = new TrackedStream(bytes);
        using var handler = new Handler((_, _) =>
        {
            var content = new NonComputableContent(stream); content.Headers.ContentType = new("image/png");
            content.Headers.ContentLength = declared;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var service = new HttpThumbnailContentService(handler);
        var result = await service.GetAsync(ImageUri, TestContext.Current.CancellationToken);
        Assert.Equal(failure, result.Failure); Assert.True(stream.Disposed);
        Assert.True(stream.BytesRead <= ThumbnailContentResult.MaximumBytes + 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HeaderDeadlineAndCallerCancellationAreDistinct(bool cancelCaller)
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(async (_, token) => { if (cancelCaller) cts.Cancel(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); });
        using var service = new HttpThumbnailContentService(handler, TimeSpan.FromMilliseconds(40));
        var result = await service.GetAsync(ImageUri, cts.Token);
        Assert.Equal(cancelCaller ? ThumbnailContentStatus.Cancelled : ThumbnailContentStatus.Failed, result.Status);
        Assert.Equal(cancelCaller ? null : ThumbnailFailure.TimedOut, result.Failure);
    }

    [Fact]
    public async Task SlowBodyDeadlineDisposesStream()
    {
        var stream = new SlowStream();
        using var handler = new Handler((_, _) =>
        {
            var content = new NonComputableContent(stream); content.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var service = new HttpThumbnailContentService(handler, TimeSpan.FromMilliseconds(50));
        Assert.Equal(ThumbnailFailure.TimedOut, (await service.GetAsync(ImageUri, TestContext.Current.CancellationToken)).Failure);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task DnsCancellationAndConnectorFailureAreObserved()
    {
        using var cts = new CancellationTokenSource(); cts.CancelAfter(40);
        var connection = new ThumbnailConnection(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return []; },
            (_, _, _) => throw new InvalidOperationException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await connection.OpenAsync("images.example.com", 443, cts.Token));
        connection = new((_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); return Stream.Null; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await connection.OpenAsync("images.example.com", 443, cts.Token));
    }

    [Fact]
    public void ResultCopiesInputAndHasNoPayloadOnFailure()
    {
        byte[] input = [1, 2, 3]; var result = ThumbnailContentResult.Success(input, ThumbnailImageKind.Jpeg); input[0] = 9;
        Assert.Equal(1, result.Content.Span[0]); Assert.Empty(ThumbnailContentResult.Failed(ThumbnailFailure.InvalidImage).Content.ToArray());
        Assert.Throws<ArgumentException>(() => ThumbnailContentResult.Success([], ThumbnailImageKind.Png));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return send(request, token); }
    }
    private class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed; public int BytesRead;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { var result = base.ReadAsync(buffer, token); if (result.IsCompletedSuccessfully) BytesRead += result.Result; return result; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class SlowStream() : TrackedStream([])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await Task.Delay(Timeout.Infinite, token); return 0; }
    }
    private sealed class NonComputableContent(Stream stream) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => stream.CopyToAsync(target);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);
        protected override void Dispose(bool disposing) { if (disposing) stream.Dispose(); base.Dispose(disposing); }
    }
}
