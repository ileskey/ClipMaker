using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

// Baseline experiments: keep the limitations of root-only exit observation reproducible.
public sealed class ProcessLifetimeSpikeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ClipMaker-Lifetime-Spike-" + Guid.NewGuid());
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ProcessFixture", "ClipMaker.ProcessFixture.exe");
    private sealed record Identity(int Id, long StartTicks);

    public ProcessLifetimeSpikeTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task TreeKillStopsKnownLiveRootChildAndGrandchild()
    {
        using var process = Start("tree");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await WaitForIdentitiesAsync(3);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        foreach (var identity in ReadIdentities()) Assert.False(IsAlive(identity));
    }

    [Fact]
    public async Task RootExitDoesNotConfirmChildExitOrPipeClosure()
    {
        using var process = Start("root-exit");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await WaitForIdentitiesAsync(2);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains(ReadIdentities(), IsAlive);
        Assert.False(stdout.IsCompleted);
        Assert.False(stderr.IsCompleted);
        // Mirror 2a's root-has-exited guard: it skips the termination request.
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        Assert.Contains(ReadIdentities(), IsAlive);
        CleanupOwnedProcesses();
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExitedIntermediateParentPreventsTreeKillFindingRemainingGrandchild()
    {
        using var process = Start("root-exit-intermediate");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await WaitForIdentitiesAsync(3);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(ReadIdentities(), IsAlive);
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        Assert.Contains(ReadIdentities(), IsAlive);
        Assert.False(stdout.IsCompleted);
        CleanupOwnedProcesses();
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private Process Start(string mode)
    {
        var info = new ProcessStartInfo(Fixture)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(root);
        return Process.Start(info)!;
    }

    private async Task WaitForIdentitiesAsync(int count)
    {
        var elapsed = Stopwatch.StartNew();
        while (ReadIdentities().Length < count)
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "Fixture identities were not recorded.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private Identity[] ReadIdentities() => Directory.GetFiles(root, "process-*.json")
        .Select(path => JsonSerializer.Deserialize<Identity>(File.ReadAllText(path))!).ToArray();

    private static bool IsAlive(Identity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Id);
            return process.StartTime.ToUniversalTime().Ticks == identity.StartTicks && !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private void CleanupOwnedProcesses()
    {
        foreach (var identity in ReadIdentities())
        {
            try
            {
                using var process = Process.GetProcessById(identity.Id);
                if (process.StartTime.ToUniversalTime().Ticks != identity.StartTicks || process.HasExited) continue;
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(5000), "Owned fixture did not exit during cleanup.");
            }
            catch (ArgumentException) { }
        }
    }

    public void Dispose()
    {
        CleanupOwnedProcesses();
        Directory.Delete(root, recursive: true);
    }
}
