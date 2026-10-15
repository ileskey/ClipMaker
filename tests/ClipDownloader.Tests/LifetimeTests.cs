using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class LifetimeTests
{
    [Fact]
    public async Task RepeatedShutdownWaitsForCancellationCleanupAndRejectsLateResults()
    {
        var diagnostics = new DelayedDiagnostics();
        var vm = new TestServices(diagnostics).Vm;
        var running = vm.RefreshToolsCommand.ExecuteAsync(null);
        await diagnostics.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var shutdown = vm.ShutdownAsync();
        Assert.Same(shutdown, vm.ShutdownAsync());
        Assert.False(shutdown.IsCompleted);
        Assert.Equal(AppLifetime.Closing, vm.Lifetime);
        Assert.False(vm.RefreshToolsCommand.CanExecute(null));
        Assert.True(diagnostics.Token.IsCancellationRequested);
        diagnostics.Completion.SetResult([]);
        await shutdown;
        await running;
        Assert.Equal(3, vm.ToolDiagnostics.Count);
        Assert.False(vm.IsBusy);
    }

    private sealed class DelayedDiagnostics : IToolDiagnosticsService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<ToolDiagnostic>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.SetResult();
            return Completion.Task;
        }
    }
}
