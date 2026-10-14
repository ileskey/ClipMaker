using ClipMaker.Media.Core.Models;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class PresentationPayloadBridgeTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("timestamp")]
    [InlineData("payload")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("cancel")]
    public async Task PayloadClockAndSameFileIdentityMustAllMatch(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "presentation-payload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "source.mp4");
        byte[] first = [0, 0, 0, 2, 0x65, 1], second = [0, 0, 0, 2, 0x65, 2];
        byte[] bytes = first.Concat(second).ToArray(); File.WriteAllBytes(path, bytes);
        try
        {
            var file = new OwnedMediaFile(Guid.NewGuid(), path, ["v"]);
            var original = HlsNativeReferenceVerifierTests.Reference(file);
            var native = original.NativeSamples!.Data!;
            var samples = native.Samples.Select((s, i) => s with { Size = 6, ByteOffset = i * 6 }).ToArray();
            var reference = original with
            {
                Reference = new IndexedCollectedReference(file, bytes.Length, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), F.Range(0, 2), F.Range(0, 2)),
                NativeSamples = OperationResult<Mp4NativeSampleEvidence>.Succeeded(new(native.Configurations, samples))
            };
            var source = new VerifiedHlsNativeReference(reference,
                [new(reference.Observations.Streams.Single(), native.Configurations.Single(), F.Range(2, 3))]);
            var normalizer = new H264PicturePayloadReader();
            var pictures = new H264CarrierPicture[]
            {
                new(fault == "missing" ? null : 180000, normalizer.Read(first, false).Data!),
                new(fault == "timestamp" ? 181501 : 181500, normalizer.Read(fault == "payload" ? first : second, false).Data!)
            };
            if (fault == "changed") File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray());
            var result = await new PresentationPayloadBridge().VerifyVideoAsync(source, new(F.Time(1, 90000), pictures),
                fault == "cancel" ? new(true) : TestContext.Current.CancellationToken);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(new PresentationPayloadEvidence(2, 2, 0), result.Data);
            }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(fault == "cancel" ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
            }
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
