using ClipDownloader.Application.Models;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class WindowClosingTests
{
    [Fact]
    public Task IdleShutdownDefersRecloseUntilClosingHasReturned() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices();
        var window = new MainWindow(s.Vm);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.SetResult();
        window.Show();
        window.Close();
        Assert.False(closed.Task.IsCompleted);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(AppLifetime.Closed, s.Vm.Lifetime);
    });

    [Fact]
    public Task RepeatedWindowCloseCannotSkipPendingCancellationCleanup() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); await s.ReadyAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<OperationResult<VideoInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Load = (_, _) => { started.SetResult(); return finish.Task; };
        var window = new MainWindow(s.Vm);
        int shutdownEvents = 0;
        window.ShutdownStarted += (_, _) => shutdownEvents++;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.SetResult();
        window.Show();
        var loading = s.Vm.LoadMetadataCommand.ExecuteAsync(null); await started.Task;
        window.Close(); window.Close();
        Assert.Equal(1, shutdownEvents);
        Assert.False(closed.Task.IsCompleted);
        Assert.Equal(AppLifetime.Closing, s.Vm.Lifetime);
        finish.SetResult(OperationResult<VideoInfo>.Succeeded(s.Info));
        await loading;
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(AppLifetime.Closed, s.Vm.Lifetime);
        Assert.Equal(MetadataState.Empty, s.Vm.MetadataState);
    });
}
