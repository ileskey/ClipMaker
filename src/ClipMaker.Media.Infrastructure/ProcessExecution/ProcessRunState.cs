namespace ClipMaker.Media.Infrastructure.ProcessExecution;

internal sealed class ProcessRunState
{
    private readonly object gate = new();
    private readonly TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ProcessFailure? failure;
    private bool complete;
    public Task FailureSignal => signal.Task;
    public ProcessFailure? Failure { get { lock (gate) return failure; } }
    public bool IsComplete { get { lock (gate) return complete; } }

    public bool TryBeginStart(bool cancellationRequested)
    {
        lock (gate)
        {
            if (complete) return false;
            if (cancellationRequested && failure is null)
                failure = new(ProcessFailureKind.UserCancellation, "Cancelled at the process startup boundary.");
            return failure is null;
        }
    }

    public void TryFail(ProcessFailure cause, bool cancellationRequested, bool timeoutElapsed)
    {
        lock (gate)
        {
            if (complete || failure is not null) return;
            failure = cancellationRequested ? new(ProcessFailureKind.UserCancellation, "User cancellation requested.") :
                timeoutElapsed ? new(ProcessFailureKind.Timeout, "Process timeout elapsed.") : cause;
            signal.TrySetResult();
        }
    }

    public ProcessFailure? Complete()
    {
        lock (gate) { complete = true; return failure; }
    }
}
