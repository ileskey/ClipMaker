using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AudioPcmRenderServiceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("conflict")]
    [InlineData("changed")]
    [InlineData("cancel")]
    public async Task RendersSelectedSourceValuesOrPreservesExistingFilesAndFailedResources(string fault)
    {
        var f = ClipProcessingQualifierTests.Fixture(ClipMode.Accurate);
        var original = f.Preparation.Value!;
        string directory = Path.GetDirectoryName(original.DecodedPcm.Path)!;
        Directory.CreateDirectory(directory);
        string outputPath = Path.Combine(directory, "render.f32le");
        byte[] bytes = Enumerable.Range(0, (int)original.DecodedPcm.Content.Bytes).Select(i => (byte)(i % 251)).ToArray();
        var identity = new MediaFileContentIdentity(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        var prepared = original with { DecodedPcm = original.DecodedPcm with { Content = identity } };
        var fact = MediaInspectionFact<AudioDecoderPreparation>.Verified(prepared, f.Preparation.Scope);
        var plan = new ClipProcessingQualifier().Qualify(f.Inspection, decoderPreparation: fact).Data!;
        var token = TestContext.Current.CancellationToken;
        await File.WriteAllBytesAsync(prepared.DecodedPcm.Path, bytes, token);
        try
        {
            if (fault == "conflict") await File.WriteAllTextAsync(outputPath, "user", token);
            if (fault == "changed") { bytes[0] ^= 1; await File.WriteAllBytesAsync(prepared.DecodedPcm.Path, bytes, token); }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (fault == "cancel") stop.Cancel();
            var service = new AudioPcmRenderService(); Guid operation = Guid.NewGuid();
            var result = await service.RenderAsync(plan, operation, Guid.NewGuid(), outputPath, stop.Token);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(bytes.Skip(48000 * 8).Take(48000 * 8), await File.ReadAllBytesAsync(outputPath, token));
                Assert.Equal(new AudioPcmReconstructionCounts(48000, 0, 0), result.Data!.Counts);
                Assert.Single(service.GetOwnedResources(operation).Files);
                Assert.True(service.GetOwnedResources(operation).NeedsCleanup);
                Assert.Equal("PcmRenderOperationReuse", (await service.RenderAsync(plan, operation, Guid.NewGuid(), outputPath, token)).Error!.Field);
            }
            else
            {
                Assert.Null(result.Data); Assert.Empty(service.GetOwnedResources(operation).Files);
                if (fault == "cancel") Assert.Equal(OperationStatus.Cancelled, result.Status);
                else Assert.Equal(fault == "changed" ? "PcmRenderIdentityBefore" : "OutputConflict", result.Error!.Field);
                if (fault == "conflict") Assert.Equal("user", await File.ReadAllTextAsync(outputPath, token));
            }
            Assert.True(File.Exists(prepared.DecodedPcm.Path));
        }
        finally { File.Delete(prepared.DecodedPcm.Path); File.Delete(outputPath); Directory.Delete(directory); }
    }
}
