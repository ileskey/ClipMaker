using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.FileSystem;
using ClipMaker.Media.Core.Models;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class OutputWorkspaceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("collision")]
    [InlineData("changed")]
    [InlineData("preserve")]
    [InlineData("unowned")]
    [InlineData("locked")]
    public async Task SessionCommitsOnceAndCleansOnlyExplicitOwnedFiles(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string workRoot = Path.Combine(directory, "jobs"); var token = TestContext.Current.CancellationToken;
        var validator = new OutputPathValidator(directory);
        var workspace = new OutputWorkspace(validator, workRoot);
        var created = await workspace.CreateAsync(Guid.NewGuid(), Path.Combine(directory, "output.mp4"), token);
        var session = created.Data!; FileStream? held = null;
        string source = Path.Combine(session.WorkDirectory, "source.mp4"), unowned = Path.Combine(session.WorkDirectory, "user.txt");
        byte[] bytes = [1, 2, 3];
        try
        {
            Assert.Equal(OperationStatus.Succeeded, created.Status); Assert.False(File.Exists(session.StagingPath));
            Assert.Equal(directory, Path.GetDirectoryName(session.StagingPath));
            await File.WriteAllBytesAsync(source, bytes, token);
            await File.WriteAllBytesAsync(session.StagingPath, bytes, token);
            var identity = new MediaFileContentIdentity(3, Convert.ToHexString(SHA256.HashData(bytes)));
            var staging = new MediaOwnedProcessingFile(session.StagingFileId, session.StagingPath, identity);
            session.TrackOwnedFiles([new(Guid.NewGuid(), source, identity), staging]);
            Assert.Throws<ArgumentException>(() => session.TrackOwnedFiles([new(Guid.NewGuid(), session.FinalPath, identity)]));
            if (fault == "collision") await File.WriteAllTextAsync(session.FinalPath, "user", token);
            if (fault == "changed") await File.WriteAllBytesAsync(session.StagingPath, [3, 2, 1], token);
            if (fault == "unowned") await File.WriteAllTextAsync(unowned, "user", token);
            if (fault == "locked") held = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fault != "preserve")
            {
                var result = await session.CommitAsync(staging);
                if (fault is "collision" or "changed")
                { Assert.Null(result.Data); Assert.Equal(fault == "collision" ? OperationErrorCode.OutputConflict : OperationErrorCode.VerificationFailed, result.Error!.Code); }
                else { Assert.Equal(OperationStatus.Succeeded, result.Status); Assert.Equal(bytes, await File.ReadAllBytesAsync(session.FinalPath, token)); }
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync(staging));
            }
            var cleanup = await session.CleanupAsync(fault == "preserve" ? WorkspaceCleanupMode.PreservePotentiallyInUse : WorkspaceCleanupMode.DeleteOwnedTemporaryFiles);
            if (fault == "preserve") { Assert.True(File.Exists(source)); Assert.True(File.Exists(session.StagingPath)); Assert.False(File.Exists(session.FinalPath)); Assert.NotEmpty(cleanup.Warnings); }
            else if (fault == "unowned") { Assert.Equal("user", await File.ReadAllTextAsync(unowned, token)); Assert.Contains(session.WorkDirectory, cleanup.RemainingPaths); }
            else if (fault == "locked") { Assert.Contains(source, cleanup.RemainingPaths); Assert.NotEmpty(cleanup.Warnings); }
            else { Assert.Empty(cleanup.RemainingPaths); Assert.Empty(cleanup.Warnings); }
            if (fault == "collision") Assert.Equal("user", await File.ReadAllTextAsync(session.FinalPath, token));
            if (fault is "success" or "locked" or "unowned") Assert.True(File.Exists(session.FinalPath));
            held?.Dispose(); held = null; if (File.Exists(unowned)) File.Delete(unowned);
            var again = await session.CleanupAsync(WorkspaceCleanupMode.DeleteOwnedTemporaryFiles);
            Assert.Empty(again.RemainingPaths);
        }
        finally
        {
            held?.Dispose(); if (File.Exists(unowned)) File.Delete(unowned); await session.CleanupAsync(WorkspaceCleanupMode.DeleteOwnedTemporaryFiles);
            File.Delete(session.FinalPath); Directory.Delete(workRoot); Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("CON.mp4")]
    [InlineData("com¹.mp4")]
    [InlineData("bad.mp4.")]
    [InlineData("bad.mp4 ")]
    [InlineData("bad:stream.mp4")]
    [InlineData("clip.mkv")]
    [InlineData("missing/clip.mp4")]
    public void InvalidWindowsOutputNamesDoNotCreateAnything(string path)
    {
        var validator = new OutputPathValidator(Path.GetFullPath(".artifacts"));
        Assert.Equal(OperationErrorCode.InvalidOutputPath, validator.Validate(path).Error!.Code);
    }
}
