using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class ProcessLifetimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ClipMaker-Lifetime-" + Guid.NewGuid());
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ProcessFixture", "ClipMaker.ProcessFixture.exe");
    private sealed record Identity(int Id, long StartTicks);
    public ProcessLifetimeTests() => Directory.CreateDirectory(root);
    private ProcessRequest Request(string mode, TimeSpan? timeout = null) =>
        new(Fixture, [mode, root], root, timeout: timeout);

    [Theory]
    [InlineData("wait")]
    [InlineData("tree")]
    public async Task CancellationConfirmsRootDescendantsAndPipesBeforeReturning(string mode)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new StringBuilder();
        Task<ProcessResult> pending = new ProcessRunner().RunAsync(Request(mode), chunk =>
        {
            if (chunk.Stream != ProcessOutputStream.StandardOutput) return;
            received.Append(chunk.Text);
            if (received.ToString().Contains("READY", StringComparison.Ordinal)) ready.TrySetResult();
        }, cancellation.Token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        cancellation.Cancel();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.Cancelled, result.Status);
        Assert.Equal(ProcessFailureKind.UserCancellation, result.Failure!.Kind);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.Null(result.CleanupFailure);
        var identities = ReadIdentities();
        if (mode == "tree") Assert.Equal(3, identities.Length);
        foreach (var identity in identities) Assert.False(IsAlive(identity));
    }

    [Theory]
    [InlineData("wait")]
    [InlineData("tree")]
    public async Task TimeoutIsSeparateFromUserCancellationAndConfirmsAllProcessExit(string mode)
    {
        var result = await new ProcessRunner().RunAsync(Request(mode, TimeSpan.FromSeconds(3)), null,
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.TimedOut, result.Status);
        Assert.Equal(ProcessFailureKind.Timeout, result.Failure!.Kind);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.Null(result.CleanupFailure);
        foreach (var identity in ReadIdentities()) Assert.False(IsAlive(identity));
    }

    [Theory]
    [InlineData("root-exit")]
    [InlineData("root-exit-silent")]
    [InlineData("root-exit-intermediate")]
    public async Task PrematureRootExitCannotBeReportedAsWholeToolSuccess(string mode)
    {
        var result = await new ProcessRunner().RunAsync(Request(mode), null, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.ExecutionFailed, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ProcessFailureKind.ProcessWaitFailed, result.Failure!.Kind);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.True(result.StandardOutput.ReachedEndOfStream);
        Assert.True(result.StandardError.ReachedEndOfStream);
        foreach (var identity in ReadIdentities()) Assert.False(IsAlive(identity));
    }

    [Fact]
    public async Task ConcurrentInvocationsDoNotInheritEachOthersPipeHandles()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProcessResult> held = new ProcessRunner().RunAsync(Request("wait"),
            chunk => { if (chunk.Text.Contains("READY", StringComparison.Ordinal)) ready.TrySetResult(); }, cancellation.Token);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var quick = await new ProcessRunner().RunAsync(new(Fixture, ["exit", "0"], root), null,
                TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(ProcessStatus.Exited, quick.Status);
            Assert.False(held.IsCompleted);
        }
        finally
        {
            cancellation.Cancel();
            await held.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ConfirmedNaturalCompletionIsNotChangedByLateCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var result = await new ProcessRunner().RunAsync(new(Fixture, ["exit", "0"], root), null, cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task ConcurrentNaturalRootExitsAreNotMistakenForOrphanedDescendants()
    {
        Task<ProcessResult>[] tasks = Enumerable.Range(0, 16).Select(index =>
            new ProcessRunner().RunAsync(new(Fixture, index % 2 == 0 ? ["directory"] : ["arguments", "a b"], root),
                null, TestContext.Current.CancellationToken)).ToArray();
        ProcessResult[] results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        foreach (var result in results)
            Assert.True(result.Status == ProcessStatus.Exited, result.Failure?.ToString());
    }

    [Fact]
    public async Task CancellationAcceptedDuringStartupWinsOverStartFailure()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runner = new ProcessRunner(_ => { cancellation.Cancel(); throw new Win32Exception(2); }, TimeSpan.FromSeconds(10));
        var result = await runner.RunAsync(Request("wait"), null, cancellation.Token);
        Assert.Equal(ProcessStatus.Cancelled, result.Status);
        Assert.Equal(ProcessFailureKind.UserCancellation, result.Failure!.Kind);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task TimeoutIncludesStartupAndRetainsItsOriginalCause()
    {
        var runner = new ProcessRunner(_ => { Thread.Sleep(100); throw new Win32Exception(2); }, TimeSpan.FromSeconds(10));
        var result = await runner.RunAsync(Request("wait", TimeSpan.FromMilliseconds(10)), null, TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.TimedOut, result.Status);
        Assert.Equal(ProcessFailureKind.Timeout, result.Failure!.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedTerminationOrAccountingOverridesNormalCancellation(bool accountingFails)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fake = new FakeSession { TerminationFails = !accountingFails, AccountingFails = accountingFails };
        var runner = new ProcessRunner(_ => fake, TimeSpan.FromMilliseconds(50));
        Task<ProcessResult> pending = runner.RunAsync(Request("wait"), null, cancellation.Token);
        cancellation.Cancel();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.TerminationFailed, result.Status);
        Assert.Equal(ProcessFailureKind.UserCancellation, result.Failure!.Kind);
        Assert.NotNull(result.CleanupFailure);
        Assert.False(result.DescendantExitConfirmed);
        if (accountingFails) Assert.True(result.RootExitConfirmed);
    }

    [Fact]
    public async Task ReadFailureRemainsPrimaryWhenUserCancelsDuringCleanup()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fake = new FakeSession(new FaultedReadStream()) { DuringTermination = cancellation.Cancel };
        var runner = new ProcessRunner(_ => fake, TimeSpan.FromSeconds(1));
        var result = await runner.RunAsync(Request("wait"), null, cancellation.Token);
        Assert.Equal(ProcessStatus.ExecutionFailed, result.Status);
        Assert.Equal(ProcessFailureKind.OutputReadFailed, result.Failure!.Kind);
        Assert.IsType<IOException>(result.Failure.Exception);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.False(result.StandardOutput.ReachedEndOfStream);
        Assert.False(result.StandardOutput.IsComplete);
    }

    private sealed class FakeSession(Stream? stdout = null) : IProcessSession
    {
        private readonly TaskCompletionSource rootExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool exited;
        public bool TerminationFails { get; init; }
        public bool AccountingFails { get; init; }
        public Action? DuringTermination { get; init; }
        public Stream StandardOutput { get; } = stdout ?? new MemoryStream();
        public Stream StandardError { get; } = new MemoryStream();
        public Task RootExit => rootExit.Task;
        public bool RootHasExited => exited;
        public int? ExitCode => exited ? 1 : null;
        public bool AllProcessesExited => AccountingFails ? throw new Win32Exception(5) : exited;
        public void Terminate()
        {
            if (TerminationFails) throw new Win32Exception(5);
            DuringTermination?.Invoke();
            exited = true;
            rootExit.TrySetResult();
        }
        public void Dispose()
        {
            rootExit.TrySetCanceled();
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }

    [Fact]
    public async Task ChildStdinEndsWithoutInheritingInteractiveConsoleInput()
    {
        var result = await new ProcessRunner().RunAsync(new(Fixture, ["stdin-eof"], root), null,
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal("0", Encoding.UTF8.GetString(result.StandardOutput.Bytes.Span));
    }

    [Fact]
    [SupportedOSPlatform("windows10.0")]
    public async Task FailureAfterNativeCreationRollsBackWholeJobBeforeStartFailed()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        var runner = new ProcessRunner(request => WindowsProcessSession.Start(request, _ =>
        {
            var elapsed = Stopwatch.StartNew();
            while (ReadIdentities().Length < 3)
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(10)) throw new TimeoutException("Tree fixture startup failed.");
                Thread.Sleep(10);
            }
            throw new Win32Exception(5);
        }), TimeSpan.FromSeconds(10));
        var result = await runner.RunAsync(Request("tree"), null, TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.StartFailed, result.Status);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.Null(result.CleanupFailure);
        Assert.Equal(3, ReadIdentities().Length);
        foreach (var identity in ReadIdentities()) Assert.False(IsAlive(identity));
    }

    [Fact]
    public async Task UnverifiedStartupRollbackNeverPretendsNoProcessWasCreated()
    {
        var runner = new ProcessRunner(_ => throw new ProcessStartCleanupException(new Win32Exception(5),
            rootExitConfirmed: true, descendantExitConfirmed: false, cleanupFailure: new Win32Exception(5)), TimeSpan.FromSeconds(10));
        var result = await runner.RunAsync(Request("wait"), null, TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.TerminationFailed, result.Status);
        Assert.True(result.RootExitConfirmed);
        Assert.False(result.DescendantExitConfirmed);
        Assert.NotNull(result.CleanupFailure);
        Assert.Equal(ProcessFailureKind.StartFailed, result.Failure!.Kind);
    }

    [Fact]
    public async Task ReaderCleanupDeadlineIsIndependentAndLateOutputCannotInvokeCallback()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var held = new HeldReadStream();
        using var fake = new FakeSession(held);
        var runner = new ProcessRunner(_ => fake, TimeSpan.FromMilliseconds(150));
        int callbacks = 0;
        Task<ProcessResult> pending = runner.RunAsync(Request("wait"), _ => Interlocked.Increment(ref callbacks), cancellation.Token);
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var elapsed = Stopwatch.StartNew();
        cancellation.Cancel();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(ProcessStatus.TerminationFailed, result.Status);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.DescendantExitConfirmed);
        Assert.False(result.StreamsDrained);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(100));
        Assert.Equal(ProcessFailureKind.UserCancellation, result.Failure!.Kind);
        Assert.NotNull(result.CleanupFailure);
        held.Release.TrySetResult();
        await held.EofRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(0, callbacks);
        Assert.Equal(0, result.StandardOutput.TotalBytes);
    }

    private sealed class HeldReadStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EofRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool delivered;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Deliberately ignores cancellation/disposal to exercise the final cleanup boundary.
            if (delivered) { EofRequested.TrySetResult(); return 0; }
            Entered.TrySetResult();
            await Release.Task;
            new byte[] { 65 }.CopyTo(buffer);
            delivered = true;
            return 1;
        }
    }

    private sealed class FaultedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Injected read failure."));
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
    public void Dispose()
    {
        foreach (var identity in ReadIdentities())
        {
            try
            {
                using var process = Process.GetProcessById(identity.Id);
                if (process.StartTime.ToUniversalTime().Ticks != identity.StartTicks || process.HasExited) continue;
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(5000));
            }
            catch (ArgumentException) { }
        }
        Directory.Delete(root, recursive: true);
    }
}
