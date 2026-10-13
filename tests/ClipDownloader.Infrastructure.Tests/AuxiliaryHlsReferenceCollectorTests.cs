using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AuxiliaryHlsReferenceCollectorTests
{
    private const string Playlist = "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6,\na.ts\n#EXT-X-ENDLIST\n";
    private sealed class Handler(string fault, CancellationTokenSource cancellation) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            bool leaf = request.RequestUri!.AbsolutePath.EndsWith("m3u8", StringComparison.Ordinal);
            if (!leaf && fault == "cancel") cancellation.Cancel();
            byte[] data = leaf ? Encoding.UTF8.GetBytes(fault == "sequence" ? Playlist.Replace("SEQUENCE:0", "SEQUENCE:1") : Playlist) : [1, 2, 3];
            return Task.FromResult(new HttpResponseMessage(fault == "http" && !leaf ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            { Content = new ByteArrayContent(data) });
        }
    }
    [Theory]
    [InlineData("success")]
    [InlineData("http")]
    [InlineData("cancel")]
    [InlineData("budget")]
    [InlineData("span")]
    [InlineData("sequence")]
    [InlineData("transport")]
    public async Task CompleteStandaloneReferencePreservesProvenanceBudgetAndOwnership(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "auxiliary-reference-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string userFile = Path.Combine(directory, "user.txt"); File.WriteAllText(userFile, "preserve");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var handler = new Handler(fault, cts); using var reader = new BoundedSourceHttpReader(handler);
            var format = new VideoFormat("270", 1920, 1080, F.Rate(30), "h264", null, StreamPresence.Present, StreamPresence.Absent,
                Transport: fault == "transport" ? FormatTransport.Https : FormatTransport.Hls);
            var locator = new SelectedMediaLocator(format, new("https://media.example/leaf.m3u8"), null, new Dictionary<string, string>());
            var budget = new SourceInspectionBudget(fault == "budget" ? 100 : 1000, F.Time(fault == "span" ? 5 : 6));
            var result = await new AuxiliaryHlsReferenceCollector(reader).CollectFirstAsync(locator, directory, budget, 100, cts.Token);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(result.Data!.File.Path));
                Assert.Equal(new[] { "270" }, result.Data.File.SourceFormatIds);
                Assert.Equal(100 + Encoding.UTF8.GetByteCount(Playlist) + 3, result.Data.BudgetUsed);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), result.Data.Sha256);
                Assert.Equal(2, handler.Calls);
            }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(fault == "cancel" ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
                Assert.Single(Directory.GetFiles(directory));
                if (fault is "budget" or "transport") Assert.Equal(0, handler.Calls);
                if (fault is "span" or "sequence") Assert.Equal(1, handler.Calls);
            }
            Assert.Empty(result.RemainingFiles);
            Assert.Equal("preserve", File.ReadAllText(userFile));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
