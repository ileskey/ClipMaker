using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SourceCollectorLifecycleTests
{
    private sealed class Handler(bool indexed, CancellationTokenSource stop, string fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (fault == "transfer-cancel") stop.Cancel();
            byte[] bytes = request.RequestUri!.AbsolutePath.EndsWith("m3u8", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:2,\na.ts\n#EXT-X-ENDLIST\n")
                : [3, 4];
            var result = new HttpResponseMessage(indexed ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            { Content = new ByteArrayContent(bytes) };
            if (indexed) result.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 11, 100);
            return Task.FromResult(result);
        }
    }

    private sealed class FaultStream(Stream inner, string fault, CancellationTokenSource stop) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken token) => fault == "flush"
            ? Task.FromException(new IOException("private flush detail")) : inner.FlushAsync(token);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (fault == "write" || fault == "delete") throw new IOException("private write detail");
            if (fault == "write-cancel") { stop.Cancel(); token.ThrowIfCancellationRequested(); }
            return inner.WriteAsync(buffer, token);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            if (fault is "close" or "close-cancel") throw new IOException("private close detail");
            if (fault == "late-cancel") stop.Cancel();
            GC.SuppressFinalize(this);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    [Theory]
    [InlineData(true, "write")]
    [InlineData(false, "write")]
    [InlineData(true, "flush")]
    [InlineData(false, "flush")]
    [InlineData(true, "close")]
    [InlineData(false, "close")]
    [InlineData(true, "delete")]
    [InlineData(false, "delete")]
    [InlineData(true, "write-cancel")]
    [InlineData(false, "write-cancel")]
    [InlineData(true, "late-cancel")]
    [InlineData(false, "late-cancel")]
    [InlineData(true, "transfer-cancel")]
    [InlineData(false, "transfer-cancel")]
    [InlineData(true, "close-cancel")]
    [InlineData(false, "close-cancel")]
    public async Task FailureOrCancellationNeverPublishesCandidateAndRetainsUncertainOwnedFiles(bool indexed, string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "source-lifecycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string user = Path.Combine(directory, "user.txt"); File.WriteAllText(user, "preserve");
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            using var reader = new BoundedSourceHttpReader(new Handler(indexed, stop, fault));
            bool deletionAttempted = false;
            Stream Create(string path)
            {
                var stream = new FaultStream(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), fault, stop);
                if (fault == "close-cancel") stop.Cancel();
                return stream;
            }
            void Delete(string path)
            {
                deletionAttempted = true;
                if (fault == "delete") throw new IOException("private delete detail");
                File.Delete(path);
            }
            var format = new VideoFormat("selected", 1920, 1080, F.Rate(30), "h264", null,
                StreamPresence.Present, StreamPresence.Absent, Transport: indexed ? FormatTransport.Https : FormatTransport.Hls);
            var locator = new SelectedMediaLocator(format, new(indexed ? "https://media.example/a.mp4" : "https://media.example/a.m3u8"), null, new Dictionary<string, string>());
            OperationStatus status;
            OperationError? error;
            IReadOnlyList<OwnedMediaFile> remaining;
            if (indexed)
            {
                var index = new Mp4SidxIndex([1, 2], 1, 1, [new(10, 2, F.Range(0, 2), true, 1, 0)]);
                var result = await new IndexedReferenceCollector(reader, Create, Delete).CollectAsync(Guid.NewGuid(), index,
                    F.Range(0, 2), locator, 100, directory, new(1000, F.Time(2)), 10, stop.Token);
                Assert.Null(result.Data);
                status = result.Status; error = result.Error; remaining = result.RemainingFiles;
            }
            else
            {
                var result = await new AuxiliaryHlsReferenceCollector(reader, Create, Delete).CollectFirstAsync(locator,
                    directory, new(1000, F.Time(2)), 10, stop.Token);
                Assert.Null(result.Data);
                status = result.Status; error = result.Error; remaining = result.RemainingFiles;
            }
            bool cancelled = fault.Contains("cancel", StringComparison.Ordinal);
            Assert.Equal(cancelled ? OperationStatus.Cancelled : OperationStatus.Failed, status);
            if (cancelled) Assert.Null(error);
            else { Assert.Equal(OperationErrorCode.OutputFailed, error!.Code); Assert.DoesNotContain("private", error.Message); }
            if (fault is "close" or "close-cancel" or "delete")
            {
                var file = Assert.Single(remaining);
                Assert.True(File.Exists(file.Path));
                Assert.Null(file.Content);
                Assert.Equal(fault is "close" or "close-cancel", file.MayBeInUse);
                Assert.Equal(fault == "delete", deletionAttempted);
            }
            else
            {
                Assert.Empty(remaining);
                Assert.Equal(user, Assert.Single(Directory.GetFiles(directory)));
            }
            Assert.Equal("preserve", File.ReadAllText(user));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
