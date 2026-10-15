using System.Windows.Threading;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Composition;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using ClipMaker.Media.Core.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class ExecutionTests
{
    [Fact]
    public async Task ResidualPathsRemainVisibleAcrossLaterResultsWithoutLockingNewClips()
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        s.Jobs.Execute = (p, _, _) => Task.FromResult(new ClipJobResult(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, ["file cleanup"], ["owned-one"]));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null); await s.WaitPathAsync();
        Assert.False(s.Vm.SessionLocked); Assert.True(s.Vm.PrepareCommand.CanExecute(null));
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        s.Jobs.Execute = (p, _, _) => Task.FromResult(new ClipJobResult(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, [], []));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Assert.Contains("owned-one", s.Vm.ResourceWarnings);
        await s.Vm.ShutdownAsync(); Assert.True(s.Vm.CanAcknowledgeClose);
    }

    [Fact]
    public Task SynchronousServicePreambleDoesNotBlockDispatcherAndShutdownWaits() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); await s.ReadyAsync();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Load = (_, token) =>
        {
            started.SetResult();
            release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            return Task.FromResult(OperationResult<VideoInfo>.Cancelled());
        };
        var load = s.Vm.LoadMetadataCommand.ExecuteAsync(null);
        try
        {
            await started.Task;
            bool responsive = false;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => responsive = true);
            Assert.True(responsive);
            var shutdown = s.Vm.ShutdownAsync(); Assert.False(shutdown.IsCompleted);
            release.Set(); await load; await shutdown;
        }
        finally { release.Set(); }
    });

    [Fact]
    public Task StageCompletedCannotOverrideFinalFailureAndLateProgressIsIgnored() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        IProgress<JobProgress>? captured = null;
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ClipJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Jobs.Execute = (p, progress, _) => { captured = progress; progress!.Report(new(p.JobId, JobStage.Completed, 100, "done")); reported.SetResult(); return finish.Task; };
        var execution = s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        await reported.Task;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(ClipState.Executing, s.Vm.JobState);
        var prepared = s.Jobs.Executed!;
        finish.SetResult(new(prepared.JobId, OperationStatus.Failed, null, new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "safe"), [], []));
        await execution;
        string finalProgress = s.Vm.ProgressText;
        captured!.Report(new(prepared.JobId, JobStage.Downloading, 10, "late"));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(ClipState.Failed, s.Vm.JobState);
        Assert.Equal(finalProgress, s.Vm.ProgressText);
        Assert.Null(s.Vm.LastResult!.OutputPath);
    });

    [Fact]
    public Task CancellationAndRepeatedCloseWaitForCleanupAndKeepCommittedSuccess() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ClipJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        s.Jobs.Execute = (_, _, t) => { token = t; started.SetResult(); return finish.Task; };
        var running = s.Vm.ConfirmExecuteCommand.ExecuteAsync(null); await started.Task;
        s.Vm.CancelCommand.Execute(null);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(ClipState.Cancelling, s.Vm.JobState);
        Assert.False(s.Vm.CanEdit);
        var shutdown = s.Vm.ShutdownAsync(); Assert.Same(shutdown, s.Vm.ShutdownAsync()); Assert.False(shutdown.IsCompleted);
        var p = s.Jobs.Executed!;
        finish.SetResult(new(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, ["cleanup warning"], ["owned-path"]));
        await running; await shutdown;
        Assert.Equal(ClipState.Completed, s.Vm.JobState);
        Assert.True(s.Vm.RequiresCloseAcknowledgement);
        Assert.True(s.Vm.CanAcknowledgeClose);
        Assert.Contains("owned-path", s.Vm.ResultText);
    });

    [Fact]
    public Task FinalizingLocksCancellationButSuccessStillComesFromReturn() => DispatcherTest.Run(async () =>
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<ClipJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Jobs.Execute = (p, progress, _) => { progress!.Report(new(p.JobId, JobStage.Finalizing, double.NaN, "commit")); reported.SetResult(); return finish.Task; };
        var running = s.Vm.ConfirmExecuteCommand.ExecuteAsync(null); await reported.Task;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(s.Vm.CancelCommand.CanExecute(null)); Assert.True(s.Vm.ProgressIndeterminate);
        var p = s.Jobs.Executed!;
        finish.SetResult(new(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, [], [])); await running;
        Assert.Equal(ClipState.Completed, s.Vm.JobState);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedTerminationOrUnexpectedExceptionLocksSession(bool unexpected)
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        s.Jobs.Execute = (p, _, _) => unexpected ? throw new InvalidOperationException("must not leak") : Task.FromResult(new ClipJobResult(p.JobId,
            OperationStatus.Failed, null, new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "safe"), [], []));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Assert.True(s.Vm.SessionLocked); Assert.False(s.Vm.PrepareCommand.CanExecute(null));
        Assert.DoesNotContain("must not leak", s.Vm.ResultText);
        Assert.True(s.Vm.LoadMetadataCommand.CanExecute(null));
    }

    [Fact]
    public async Task PresentationKeepsExactUserRangesAndSampleCounts()
    {
        var s = new TestServices(); await s.ReadyAsync(); await s.Vm.PrepareCommand.ExecuteAsync(null);
        SignedMediaTime.TryCreate(1, 3, out var start, out _); SignedMediaTime.TryCreate(31, 3, out var end, out _);
        PresentationRange.TryCreate(start!, end!, out var range, out _);
        s.Jobs.Execute = (p, _, _) => Task.FromResult(new ClipJobResult(p.JobId, OperationStatus.Succeeded, p.FinalPath, null, [], [], new(range!, range!, null, false, 1, 2, 3, 4)));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Assert.Contains("1/3", s.Vm.ResultText); Assert.Contains("31/3", s.Vm.ResultText);
        Assert.Contains("음성 범위 없음", s.Vm.ResultText); Assert.Contains("원본 전체 끝 미확인", s.Vm.ResultText);
        Assert.Contains("zero 1 / 제거 overlap 2 / priming skip 3 / decoded tail 4", s.Vm.ResultText);
    }

    [Fact]
    public Task RealCompositionAndXamlCanBeCreatedOnDispatcher() => DispatcherTest.Run(async () =>
    {
        using var owner = ServiceRegistration.Create(null);
        Assert.NotNull(owner.GetRequiredService<IClipJobService>());
        var vm = owner.GetRequiredService<MainViewModel>();
        var window = owner.GetRequiredService<MainWindow>();
        Assert.Same(vm, window.DataContext);
        await vm.ShutdownAsync(); window.Close();
    });
}
