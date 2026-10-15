namespace ClipMaker.Media.Infrastructure.ProcessExecution;

public enum ProcessOutputMode { Full, Tail, Drain }
public enum ProcessOutputStream { StandardOutput, StandardError }
public enum ProcessStatus { Exited, StartFailed, Cancelled, TimedOut, ExecutionFailed, TerminationFailed }
public enum ProcessFailureKind
{
    StartFailed, OutputLimitExceeded, OutputReadFailed, CallbackFailed,
    ProcessWaitFailed, UserCancellation, Timeout, CleanupFailed
}

public sealed class ProcessRequest
{
    public string ExecutablePath { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string WorkingDirectory { get; }
    public TimeSpan? Timeout { get; }
    public ProcessOutputMode StandardOutputMode { get; }
    public ProcessOutputMode StandardErrorMode { get; }

    public ProcessRequest(string executablePath, IEnumerable<string> arguments, string workingDirectory,
        ProcessOutputMode standardOutputMode = ProcessOutputMode.Tail,
        ProcessOutputMode standardErrorMode = ProcessOutputMode.Tail, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!Path.IsPathFullyQualified(executablePath))
            throw new ArgumentException("Executable path must be absolute.", nameof(executablePath));
        if (!Path.IsPathFullyQualified(workingDirectory))
            throw new ArgumentException("Working directory must be absolute.", nameof(workingDirectory));
        if (!Enum.IsDefined(standardOutputMode))
            throw new ArgumentOutOfRangeException(nameof(standardOutputMode));
        if (!Enum.IsDefined(standardErrorMode))
            throw new ArgumentOutOfRangeException(nameof(standardErrorMode));
        if (timeout is { } limit && (limit <= TimeSpan.Zero || limit.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must fit a positive timer interval.");
        string[] snapshot = arguments.ToArray();
        if (snapshot.Any(argument => argument is null || argument.Contains('\0')))
            throw new ArgumentException("Arguments must not contain null values or NUL characters.", nameof(arguments));
        ExecutablePath = Path.GetFullPath(executablePath);
        Arguments = Array.AsReadOnly(snapshot);
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        StandardOutputMode = standardOutputMode;
        StandardErrorMode = standardErrorMode;
        Timeout = timeout;
    }
}

public sealed record ProcessOutputChunk(ProcessOutputStream Stream, string Text);
public sealed record ProcessFailure(ProcessFailureKind Kind, string Description, Exception? Exception = null);

// Infrastructure-only raw bytes: callers must check completeness and limits before parsing JSON.
public sealed record ProcessOutput(
    ReadOnlyMemory<byte> Bytes, long TotalBytes, bool IsTruncated, bool LimitExceeded,
    bool IsComplete, bool ReachedEndOfStream);

public sealed record ProcessResult(
    ProcessStatus Status, int? ExitCode, ProcessOutput StandardOutput, ProcessOutput StandardError,
    ProcessFailure? Failure, bool RootExitConfirmed, bool StreamsDrained,
    bool? DescendantExitConfirmed, ProcessFailure? CleanupFailure);
