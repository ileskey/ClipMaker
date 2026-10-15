using ClipMaker.Media.Infrastructure.ProcessExecution;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class ProcessRunStateTests
{
    [Theory]
    [InlineData(true, true, ProcessFailureKind.UserCancellation)]
    [InlineData(false, true, ProcessFailureKind.Timeout)]
    [InlineData(false, false, ProcessFailureKind.OutputReadFailed)]
    public void SimultaneouslyObservedCausesUseDefinedPriority(bool cancellation, bool timeout, ProcessFailureKind expected)
    {
        var state = new ProcessRunState();
        state.TryFail(new(ProcessFailureKind.OutputReadFailed, "read"), cancellation, timeout);
        Assert.Equal(expected, state.Complete()!.Kind);
    }

    [Fact]
    public void FirstAcceptedCauseSurvivesRepeatedAndLaterRequests()
    {
        var state = new ProcessRunState();
        state.TryFail(new(ProcessFailureKind.Timeout, "timeout"), false, true);
        state.TryFail(new(ProcessFailureKind.UserCancellation, "late cancellation"), true, true);
        Assert.Equal(ProcessFailureKind.Timeout, state.Complete()!.Kind);
    }

    [Fact]
    public void CompletionClosesTheCancellationBoundaryAndPreventsAnotherStart()
    {
        var state = new ProcessRunState();
        Assert.True(state.TryBeginStart(false));
        Assert.Null(state.Complete());
        state.TryFail(new(ProcessFailureKind.UserCancellation, "late"), true, false);
        Assert.Null(state.Failure);
        Assert.False(state.TryBeginStart(false));
    }

    [Fact]
    public void AlreadyCancelledStartupNeverOpensTheStartBoundary()
    {
        var state = new ProcessRunState();
        Assert.False(state.TryBeginStart(true));
        Assert.Equal(ProcessFailureKind.UserCancellation, state.Complete()!.Kind);
    }
}
