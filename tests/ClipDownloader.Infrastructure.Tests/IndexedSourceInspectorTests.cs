using System.Net;
using System.Net.Http.Headers;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class IndexedSourceInspectorTests
{
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var range = request.Headers.Range!.Ranges.Single();
            byte[] body;
            if (range.From == 0)
            {
                body = new byte[65536];
                var prefix = Mp4SidxReaderTests.Prefix();
                prefix.CopyTo(body, 0);
                // A media box may extend beyond this complete HTTP prefix.
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(prefix.Length), 100000);
                "mdat"u8.CopyTo(body.AsSpan(prefix.Length + 4));
            }
            else body = new byte[100];
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, 100000);
            return Task.FromResult(response);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("foreign")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("termination")]
    public async Task PrefixIdentityFlowsToLocalProbeAndFailureRetainsOwnedFile(string outcome)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "indexed-inspector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var handler = new Handler();
            using var reader = new BoundedSourceHttpReader(handler);
            var format = new VideoFormat("v", 1920, 1080, F.Rate(), "h264", null, StreamPresence.Present, StreamPresence.Absent);
            var locator = new SelectedMediaLocator(format, new("https://media.example/video.mp4"), null, new Dictionary<string, string>());
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var inspector = new IndexedSourceInspector(reader, (reference, token) =>
            {
                Assert.IsType<IndexedCollectedReference>(reference);
                Assert.Equal(120, new FileInfo(reference.File.Path).Length);
                if (outcome is "cancelled" or "termination") cts.Cancel();
                return Task.FromResult(outcome switch
                {
                    "failed" or "termination" => OperationResult<ProbedInitialReference>.Failed(new(
                        outcome == "termination" ? OperationErrorCode.ProcessTerminationFailed : OperationErrorCode.ProcessFailed,
                        OperationErrorCategory.Execution, "safe")),
                    "cancelled" => OperationResult<ProbedInitialReference>.Cancelled(),
                    _ => OperationResult<ProbedInitialReference>.Succeeded(HlsNativeReferenceVerifierTests.Reference() with
                    { Reference = outcome == "foreign" ? HlsNativeReferenceVerifierTests.Reference().Reference : reference })
                });
            });
            var result = await inspector.InspectInitialAsync(locator, directory, new(70000, F.Time(2)), 10, cts.Token);
            Assert.Equal(2, handler.Calls);
            Assert.Single(result.RemainingFiles);
            Assert.True(File.Exists(result.RemainingFiles[0].Path));
            Assert.NotNull(result.Source);
            if (outcome == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(65646, result.Data!.Reference.BudgetUsed);
            }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(outcome == "cancelled" ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
                if (outcome == "termination") Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code);
                if (outcome == "foreign") Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
