using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class IndexedReferenceCollectorTests
{
    private sealed class Handler(int fault) : HttpMessageHandler
    {
        internal List<(long?, long?)> Ranges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var range = request.Headers.Range!.Ranges.Single();
            Ranges.Add((range.From, range.To));
            var response = new HttpResponseMessage(fault == 1 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            { Content = new ByteArrayContent(fault == 3 ? [3] : [3, 4]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, fault == 2 ? 101 : 100);
            return Task.FromResult(response);
        }
    }
    private static SignedMediaTime Time(int n)
    { SignedMediaTime.TryCreate(n, 1, out var t, out _); return t!; }
    private static PresentationRange Range(int start, int end)
    { PresentationRange.TryCreate(Time(start), Time(end), out var r, out _); return r!; }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CollectsExactSelectedRangesOrFailsWithoutCandidate(int fault)
    {
        string dir = Path.Combine(Path.GetFullPath(".artifacts"), "indexed-collector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid();
        string path = Path.Combine(dir, $"indexed-ref-{id:N}.mp4");
        string userFile = Path.Combine(dir, "user.txt");
        File.WriteAllText(userFile, "preserve");
        if (fault == 6) File.WriteAllText(path, "existing");
        try
        {
            var handler = new Handler(fault);
            using var reader = new BoundedSourceHttpReader(handler);
            var index = new Mp4SidxIndex([1, 2], 1, 1,
                [new(10, 2, Range(0, 2), true, 1, 0), new(20, 2, Range(2, 4), true, 1, 0)]);
            var format = new VideoFormat("selected", 1920, 1080, null, "h264", null,
                StreamPresence.Present, StreamPresence.Absent);
            var selected = new SelectedMediaLocator(format, new Uri("https://media.example/a.mp4"), null, new Dictionary<string, string>());
            using var cts = new CancellationTokenSource();
            if (fault == 5) cts.Cancel();
            var result = await new IndexedReferenceCollector(reader).CollectAsync(id, index, Range(1, 3), selected,
                100, dir, new(fault == 4 ? 13 : 14, Time(4)), 10, cts.Token);
            if (fault == 0)
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(new byte[] { 1, 2, 3, 4, 3, 4 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
                Assert.Equal(14, result.Data!.BudgetUsed);
                Assert.Equal(6, result.Data.Bytes);
                Assert.Equal(Range(0, 4), result.Data.ActualIndexRange);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))).ToLowerInvariant(), result.Data.Sha256);
                Assert.Equal(new (long?, long?)[] { (10, 11), (20, 21) }, handler.Ranges);
            }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(fault == 5 ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
                if (fault == 6) Assert.Equal("existing", File.ReadAllText(path));
                else Assert.False(File.Exists(path));
                if (fault >= 4) Assert.Empty(handler.Ranges);
                Assert.Empty(result.RemainingFiles);
            }
            Assert.Equal("preserve", File.ReadAllText(userFile));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(dir)) File.Delete(file);
            Directory.Delete(dir);
        }
    }
}
