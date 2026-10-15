using ClipDownloader.Application.Models;
using ClipDownloader.ViewModels;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class PreparationTests
{
    [Theory]
    [InlineData("url")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("quality")]
    [InlineData("mode")]
    [InlineData("output")]
    public async Task EveryInputChangeInvalidatesConfirmation(string field)
    {
        var s = new TestServices(); await s.ReadyAsync();
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        Assert.True(s.Vm.ShowingConfirmation);
        switch (field)
        {
            case "url": s.Vm.Url = "https://youtu.be/abcdefghijk"; break;
            case "start": s.Vm.Start.Frames = "01"; break;
            case "end": s.Vm.End.Frames = "01"; break;
            case "quality": s.Vm.Quality = s.Vm.Qualities.Single(q => q.Height == 720); break;
            case "mode": s.Vm.Mode = ClipMode.Accurate; break;
            case "output": s.Vm.OutputPath += ".mp4"; break;
        }
        Assert.False(s.Vm.ShowingConfirmation);
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Assert.Equal(0, s.Jobs.ExecuteCalls);
        await s.Vm.ShutdownAsync();
    }

    [Fact]
    public async Task ConfirmUsesSamePreparedInstanceOnceAndNewRunRequiresPreparation()
    {
        var s = new TestServices(); await s.ReadyAsync();
        Assert.False(File.Exists(s.Vm.OutputPath));
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        var prepared = s.Jobs.Prepared;
        Assert.NotNull(prepared);
        Assert.Contains(prepared.FinalPath, s.Vm.ConfirmationText);
        Assert.False(File.Exists(s.Vm.OutputPath));
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        await s.Vm.ConfirmExecuteCommand.ExecuteAsync(null);
        Assert.Same(prepared, s.Jobs.Executed);
        Assert.Equal(1, s.Jobs.ExecuteCalls);
        await s.WaitPathAsync();
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        Assert.NotEqual(prepared.JobId, s.Jobs.Prepared!.JobId);
    }

    [Fact]
    public async Task CancelledLatePrepareCannotBecomeConfirmableAndDuplicateEntryIsGuarded()
    {
        var s = new TestServices(); await s.ReadyAsync();
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        var old = s.Jobs.Prepared!; s.Vm.BackCommand.Execute(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<OperationResult<PreparedClip>>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Jobs.Prepare = (_, _, _) => { started.SetResult(); return delayed.Task; };
        var preparing = s.Vm.PrepareCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await s.Vm.PrepareCommand.ExecuteAsync(null);
        Assert.Equal(2, s.Jobs.PrepareCalls);
        s.Vm.CancelCommand.Execute(null);
        Assert.False(s.Vm.CanEdit);
        delayed.SetResult(OperationResult<PreparedClip>.Succeeded(old));
        await preparing;
        Assert.False(s.Vm.ShowingConfirmation);
        Assert.Equal(ClipState.Cancelled, s.Vm.JobState);
    }
}
