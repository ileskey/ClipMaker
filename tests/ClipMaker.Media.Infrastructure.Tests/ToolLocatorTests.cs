using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class ToolLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ClipMaker-Phase0-" + Guid.NewGuid());
    private const string Name = "tool.exe";

    public ToolLocatorTests() => Directory.CreateDirectory(root);

    private string FileAt(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private ToolLocator Locator(IEnumerable<string>? path = null, Func<string, FileAttributes>? inspect = null) =>
        new(new ToolSearchEnvironment(root, path ?? []), inspect);

    [Fact]
    public void ExplicitPathWinsOverToolsAndPath()
    {
        var user = FileAt("user/tool.exe");
        FileAt("tools/tool.exe");
        FileAt("path/tool.exe");
        var result = Locator(["path"]).Locate(new(Name, user), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.Found, result.Status);
        Assert.Equal(user, result.SelectedPath);
        Assert.Equal(ToolLocationSource.UserSpecified, result.Source);
        Assert.True(result.IsUserSpecified);
        Assert.Single(result.Inspections);
    }

    [Fact]
    public void ToolsWinsOverPath()
    {
        var tools = FileAt("tools/tool.exe");
        FileAt("path/tool.exe");
        var result = Locator(["path"]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(tools, result.SelectedPath);
        Assert.Equal(ToolLocationSource.BundledTools, result.Source);
        Assert.False(result.IsUserSpecified);
    }

    [Fact]
    public void PathOrderAndQuotedRelativeEntriesArePreserved()
    {
        var first = FileAt("first/tool.exe");
        FileAt("second/tool.exe");
        var result = Locator([" ", "  \"first\"  ", "second"]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(first, result.SelectedPath);
        Assert.Equal(ToolLocationSource.Path, result.Source);
        Assert.Equal(2, result.Inspections.Count);
    }

    [Fact]
    public void ExplicitRelativePathUsesBaseDirectory()
    {
        var file = FileAt("relative/tool.exe");
        Assert.Equal(file, Locator().Locate(new(Name, "relative/tool.exe"), TestContext.Current.CancellationToken).SelectedPath);
    }

    [Fact]
    public void MissingExplicitPathNeverFallsBack()
    {
        FileAt("tools/tool.exe");
        var result = Locator().Locate(new(Name, "absent.exe"), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.Missing, result.Status);
        Assert.True(result.IsUserSpecified);
        Assert.Single(result.Inspections);
        Assert.Equal(Path.Combine(root, "absent.exe"), result.Inspections[0].Path);
        Assert.Null(result.SelectedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("C:tool.exe")]
    [InlineData("\\tool.exe")]
    [InlineData("bad\0path")]
    public void InvalidExplicitPathFailsWithoutFallback(string path)
    {
        FileAt("tools/tool.exe");
        var result = Locator().Locate(new(Name, path), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.SearchFailed, result.Status);
        Assert.True(result.IsUserSpecified);
        Assert.Single(result.Inspections);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void ExplicitDirectoryIsSearchFailure()
    {
        var result = Locator().Locate(new(Name, root), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.SearchFailed, result.Status);
        Assert.Contains("폴더", result.FailureReason!);
    }

    [Fact]
    public void DirectoryAtDefaultCandidateIsMissingAndSearchContinues()
    {
        Directory.CreateDirectory(Path.Combine(root, "tools", Name));
        var file = FileAt("path/tool.exe");
        var result = Locator(["path"]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(file, result.SelectedPath);
        Assert.Equal(ToolLocationStatus.Missing, result.Inspections[0].Status);
    }

    [Fact]
    public void EmptyPathAndMissingToolsReturnMissing()
    {
        var result = Locator(["", " "]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.Missing, result.Status);
        Assert.Null(result.FailureReason);
        Assert.Single(result.Inspections);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultCandidateFailureContinuesAndPreservesReason(bool laterFound)
    {
        var file = laterFound ? FileAt("path/tool.exe") : Path.Combine(root, "path", Name);
        var result = Locator(["path"], path =>
        {
            if (path.Contains(Path.DirectorySeparatorChar + "tools" + Path.DirectorySeparatorChar))
                throw new UnauthorizedAccessException("injected access failure");
            return File.GetAttributes(path);
        }).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(laterFound ? ToolLocationStatus.Found : ToolLocationStatus.SearchFailed, result.Status);
        Assert.Equal(ToolLocationStatus.SearchFailed, result.Inspections[0].Status);
        Assert.Contains("injected access failure", result.Inspections[0].FailureReason!);
        Assert.Equal(file, result.Inspections[1].Path);
        if (laterFound) Assert.Equal(file, result.SelectedPath);
        else Assert.Contains("UnauthorizedAccessException", result.FailureReason!);
    }

    [Fact]
    public void ExplicitInspectionFailureNeverFallsBack()
    {
        int calls = 0;
        var result = Locator(["path"], _ =>
        {
            calls++;
            throw new IOException("injected IO failure");
        }).Locate(new(Name, "user.exe"), TestContext.Current.CancellationToken);
        Assert.Equal(ToolLocationStatus.SearchFailed, result.Status);
        Assert.Equal(1, calls);
        Assert.Contains("injected IO failure", result.FailureReason!);
    }

    [Fact]
    public void InvalidPathCandidateCanBeFollowedByFoundFile()
    {
        var file = FileAt("path/tool.exe");
        var result = Locator(["C:relative", "path"]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(file, result.SelectedPath);
        Assert.Equal(ToolLocationStatus.SearchFailed, result.Inspections[1].Status);
        Assert.Equal(ToolLocationSource.Path, result.Inspections[1].Source);
    }

    [Fact]
    public void DuplicateNormalizedCandidatesAreInspectedOnce()
    {
        var result = Locator(["path", "./path", "PATH"]).Locate(new(Name), TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Inspections.Count);
    }

    [Fact]
    public void CancellationBetweenCandidatesIsNotSearchFailure()
    {
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var locator = Locator(["path"], _ =>
        {
            calls++;
            cancellation.Cancel();
            throw new FileNotFoundException();
        });
        Assert.Throws<OperationCanceledException>(() => locator.Locate(new(Name), cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AlreadyCancelledRequestDoesNotInspectFiles()
    {
        var locator = Locator(inspect: _ => throw new InvalidOperationException("must not inspect"));
        Assert.Throws<OperationCanceledException>(() => locator.Locate(new(Name), new CancellationToken(true)));
    }

    [Fact]
    public void ProgrammingErrorsAreNotHiddenAsMissing()
    {
        var locator = Locator(inspect: _ => throw new InvalidOperationException("injected bug"));
        Assert.Throws<InvalidOperationException>(() => locator.Locate(new(Name), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EnvironmentAndSettingsSnapshotTheirInputs()
    {
        var paths = new List<string> { "first" };
        var environment = new ToolSearchEnvironment(root, paths);
        paths[0] = "changed";
        var input = new Dictionary<string, string?> { [Name] = "user.exe" };
        var settings = new ToolPathSettings(input);
        input[Name] = "changed";
        Assert.Equal("first", Assert.Single(environment.PathDirectories));
        Assert.Equal("user.exe", settings.GetPath("TOOL.EXE"));
        Assert.Null(settings.GetPath("unset.exe"));
    }

    public void Dispose()
    {
        // Only remove the unique temporary tree created by this test instance.
        Directory.Delete(root, recursive: true);
    }
}

