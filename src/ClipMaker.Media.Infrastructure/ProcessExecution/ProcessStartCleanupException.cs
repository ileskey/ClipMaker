namespace ClipMaker.Media.Infrastructure.ProcessExecution;

internal sealed class ProcessStartCleanupException(
    Exception startFailure, bool rootExitConfirmed, bool descendantExitConfirmed, Exception? cleanupFailure)
    : Exception("Started process could not be verified as terminated after startup failure.", startFailure)
{
    public bool RootExitConfirmed { get; } = rootExitConfirmed;
    public bool DescendantExitConfirmed { get; } = descendantExitConfirmed;
    public Exception? CleanupFailure { get; } = cleanupFailure;
}
