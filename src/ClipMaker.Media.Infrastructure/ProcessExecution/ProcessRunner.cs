using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClipMaker.Media.Infrastructure.ProcessExecution;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, IProcessSession> startProcess;
    private readonly TimeSpan cleanupTimeout;

    public ProcessRunner() : this(StartWindowsProcess, TimeSpan.FromSeconds(10)) { }

    // Instance-scoped seams permit deterministic OS-failure tests, not runtime validation flags.
    internal ProcessRunner(Func<ProcessRequest, IProcessSession> startProcess, TimeSpan cleanupTimeout)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        if (cleanupTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cleanupTimeout));
        this.startProcess = startProcess;
        this.cleanupTimeout = cleanupTimeout;
    }

    private static IProcessSession StartWindowsProcess(ProcessRequest request) =>
        OperatingSystem.IsWindowsVersionAtLeast(10) ? WindowsProcessSession.Start(request) :
            throw new PlatformNotSupportedException("Verified process containment requires Windows 10 or later.");

    public async Task<ProcessResult> RunAsync(ProcessRequest request,
        Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stdout = new ProcessOutputCollector(request.StandardOutputMode);
        var stderr = new ProcessOutputCollector(request.StandardErrorMode);
        var state = new ProcessRunState();
        if (cancellationToken.IsCancellationRequested)
            return WithoutProcess(ProcessStatus.Cancelled,
                new(ProcessFailureKind.UserCancellation, "Cancelled before process start."));

        using var timeout = new CancellationTokenSource();
        using var stopReading = new CancellationTokenSource();
        CancellationToken readToken = stopReading.Token;
        void Fail(ProcessFailure failure) => state.TryFail(failure, cancellationToken.IsCancellationRequested,
            timeout.IsCancellationRequested);
        using var cancellationRegistration = cancellationToken.Register(() =>
            Fail(new(ProcessFailureKind.UserCancellation, "User cancellation requested.")));
        using var timeoutRegistration = timeout.Token.Register(() =>
            Fail(new(ProcessFailureKind.Timeout, "Process timeout elapsed.")));
        if (!state.TryBeginStart(cancellationToken.IsCancellationRequested))
            return WithoutProcess(ProcessStatus.Cancelled, state.Complete());
        if (request.Timeout is { } interval) timeout.CancelAfter(interval);
        IProcessSession session;
        try { session = startProcess(request); }
        catch (ProcessStartCleanupException exception)
        {
            Fail(new(ProcessFailureKind.StartFailed, "Process initialization failed after creation.", exception.InnerException));
            return new(ProcessStatus.TerminationFailed, null, stdout.Snapshot(), stderr.Snapshot(), state.Complete(),
                exception.RootExitConfirmed, true, exception.DescendantExitConfirmed,
                new(ProcessFailureKind.CleanupFailed, "Startup rollback could not verify process termination.", exception.CleanupFailure));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException
            or TimeoutException or PlatformNotSupportedException)
        {
            Fail(new(ProcessFailureKind.StartFailed, "Process could not start.", exception));
            ProcessFailure? failure = state.Complete();
            return WithoutProcess(StatusFor(failure), failure);
        }
        using (session)
        {
            Task outputReader = DrainAsync(session.StandardOutput, stdout, ProcessOutputStream.StandardOutput);
            Task errorReader = DrainAsync(session.StandardError, stderr, ProcessOutputStream.StandardError);
            Task rootWait = WaitForRootAsync();
            Task finished = Task.WhenAll(rootWait, outputReader, errorReader);
            await Task.WhenAny(rootWait, state.FailureSignal).ConfigureAwait(false);
            var cleanupClock = Stopwatch.StartNew();
            ProcessFailure? cleanupFailure = null;
            bool terminationRequested = false;
            bool descendantsExited = false;
            bool rootExited = false;
            bool readersStopped = false;
            int? exitCode = null;

            while (true)
            {
                if (state.Failure is not null && !terminationRequested)
                {
                    terminationRequested = true;
                    try { session.Terminate(); }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                    {
                        cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Process-group termination request failed.", exception);
                    }
                }
                try
                {
                    descendantsExited = session.AllProcessesExited;
                }
                catch (Win32Exception exception)
                {
                    cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Process-group exit could not be verified.", exception);
                    Fail(cleanupFailure);
                }
                try
                {
                    rootExited = session.RootHasExited;
                    if (rootExited) exitCode = session.ExitCode;
                    // An exit notification can precede retirement of other job members.
                    // Reserve half the same cleanup budget for terminating persistent members.
                    if (state.Failure is null && rootExited && !descendantsExited &&
                        cleanupClock.Elapsed >= cleanupTimeout / 2)
                        Fail(new(ProcessFailureKind.ProcessWaitFailed, "Root exited but its process group did not finish naturally."));
                }
                catch (Win32Exception exception)
                {
                    cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Root exit could not be verified.", exception);
                    Fail(cleanupFailure);
                }
                readersStopped = outputReader.IsCompletedSuccessfully && errorReader.IsCompletedSuccessfully;
                if (rootExited && descendantsExited && finished.IsCompletedSuccessfully) break;
                TimeSpan remaining = cleanupTimeout - cleanupClock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Process/stream cleanup exceeded its independent deadline.");
                    break;
                }
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(20) ? remaining : TimeSpan.FromMilliseconds(20))
                    .ConfigureAwait(false);
            }

            stopReading.Cancel();
            try { session.StandardOutput.Dispose(); session.StandardError.Dispose(); }
            catch (IOException exception)
            {
                cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Output stream disposal failed.", exception);
            }
            ProcessFailure? primary = state.Complete();
            ProcessStatus status = cleanupFailure is not null || !rootExited || !descendantsExited || !readersStopped
                ? ProcessStatus.TerminationFailed : StatusFor(primary);
            _ = finished.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return new(status, exitCode, stdout.Snapshot(), stderr.Snapshot(), primary,
                rootExited, readersStopped, descendantsExited, cleanupFailure);

            async Task WaitForRootAsync()
            {
                try { await session.RootExit.ConfigureAwait(false); }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException)
                {
                    Fail(new(ProcessFailureKind.ProcessWaitFailed, "Could not observe root exit.", exception));
                }
            }
        }

        ProcessResult WithoutProcess(ProcessStatus status, ProcessFailure? failure) =>
            new(status, null, stdout.Snapshot(), stderr.Snapshot(), failure, true, true, true, null);

        async Task DrainAsync(Stream stream, ProcessOutputCollector collector, ProcessOutputStream streamKind)
        {
            byte[] buffer = new byte[8192];
            char[] characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
            Decoder decoder = Encoding.UTF8.GetDecoder();
            bool callbackEnabled = outputReceived is not null;
            try
            {
                while (true)
                {
                    int count = await stream.ReadAsync(buffer, readToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        Deliver(decoder.GetChars([], characters, flush: true));
                        collector.MarkComplete();
                        return;
                    }
                    if (!collector.Add(buffer.AsSpan(0, count)))
                        Fail(new(ProcessFailureKind.OutputLimitExceeded, $"{streamKind} exceeded the 32MiB capture limit."));
                    if (callbackEnabled)
                        Deliver(decoder.GetChars(buffer.AsSpan(0, count), characters, flush: false));
                }
            }
            catch (OperationCanceledException) when (stopReading.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OverflowException)
            {
                Fail(new(ProcessFailureKind.OutputReadFailed, $"Could not drain {streamKind}.", exception));
            }

            void Deliver(int count)
            {
                if (!callbackEnabled || count == 0 || state.IsComplete) return;
                try
                {
                    for (int offset = 0; offset < count && !state.IsComplete;)
                    {
                        int length = Math.Min(8192, count - offset);
                        if (offset + length < count && char.IsHighSurrogate(characters[offset + length - 1])) length--;
                        outputReceived!(new(streamKind, new string(characters, offset, length)));
                        offset += length;
                    }
                }
                catch (Exception exception)
                {
                    callbackEnabled = false;
                    Fail(new(ProcessFailureKind.CallbackFailed, "Output receiver failed.", exception));
                }
            }
        }
    }

    private static ProcessStatus StatusFor(ProcessFailure? failure) => failure?.Kind switch
    {
        null => ProcessStatus.Exited,
        ProcessFailureKind.StartFailed => ProcessStatus.StartFailed,
        ProcessFailureKind.UserCancellation => ProcessStatus.Cancelled,
        ProcessFailureKind.Timeout => ProcessStatus.TimedOut,
        _ => ProcessStatus.ExecutionFailed
    };
}
