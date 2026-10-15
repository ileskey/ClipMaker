using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SourceStartPrefixInspectorTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("prefix")]
    [InlineData("first-changed")]
    [InlineData("target-changed")]
    [InlineData("missing-content")]
    [InlineData("cancel")]
    public async Task SourceStartNeedsActualCapturedPrefixCorrespondenceAndPreservesFiles(string fault)
    {
        var f = new F(); Directory.CreateDirectory(f.DirectoryPath);
        var firstBytes = new byte[] { 1, 2, 3 };
        var targetBytes = fault == "prefix" ? new byte[] { 3, 2, 1, 4, 5 } : [1, 2, 3, 4, 5];
        var first = new OwnedMediaFile(f.File.Id, f.File.Path, ["v"],
            fault == "missing-content" ? null : new(firstBytes.Length, Convert.ToHexString(SHA256.HashData(firstBytes))));
        var target = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "target.mp4"), ["v"],
            new(targetBytes.Length, Convert.ToHexString(SHA256.HashData(targetBytes))));
        string user = Path.Combine(f.DirectoryPath, "user.txt");
        File.WriteAllText(user, "preserve"); File.WriteAllBytes(first.Path, firstBytes); File.WriteAllBytes(target.Path, targetBytes);
        try
        {
            var request = f.Request(f.Inspect(files: [first]));
            var acquired = new AcquiredMedia(Guid.NewGuid(), request, [target], []);
            if (fault == "first-changed") File.WriteAllBytes(first.Path, [3, 2, 1]);
            if (fault == "target-changed") File.WriteAllBytes(target.Path, [5, 4, 3, 2, 1]);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            if (fault == "cancel") stop.Cancel();
            var result = await new SourceStartPrefixInspector().InspectAsync(acquired, target, stop.Token);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(first.Id, result.Data!.SourceFirstFileId);
                Assert.Equal(target.Id, result.Data.TargetFileId);
                Assert.Equal(first.Content!.Sha256, result.Data.PrefixSha256);
            }
            else if (fault == "cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Data); }
            else { Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.Data); }
            Assert.True(File.Exists(first.Path)); Assert.True(File.Exists(target.Path));
            Assert.Equal("preserve", File.ReadAllText(user));
        }
        finally
        {
            File.Delete(first.Path); File.Delete(target.Path); File.Delete(user); Directory.Delete(f.DirectoryPath);
        }
    }
}
