using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClipMaker.Media.Infrastructure.ProcessExecution;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public async Task<ProcessResult> RunAsync(ProcessRequest request,
        Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stdout = new ProcessOutputCollector(request.StandardOutputMode);
        var stderr = new ProcessOutputCollector(request.StandardErrorMode);
        if (cancellationToken.IsCancellationRequested)
            return new(ProcessStatus.Cancelled, null, stdout.Snapshot(), stderr.Snapshot(),
                new(ProcessFailureKind.UserCancellation, "Cancelled before process start."), true, true, true, null);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.WorkingDirectory
        };
        foreach (string argument in request.Arguments) process.StartInfo.ArgumentList.Add(argument);

        using var timeout = new CancellationTokenSource();
        using var stopReading = new CancellationTokenSource();
        var failureSignal = new TaskCompletionSource<ProcessFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Fail(ProcessFailure failure)
        {
            if (cancellationToken.IsCancellationRequested)
                failure = new(ProcessFailureKind.UserCancellation, "User cancellation requested.");
            else if (timeout.IsCancellationRequested)
                failure = new(ProcessFailureKind.Timeout, "Process timeout elapsed.");
            failureSignal.TrySetResult(failure);
        }

        // This timer includes process startup and is independent of cleanup cancellation.
        if (request.Timeout is { } interval) timeout.CancelAfter(interval);
        using var cancellationRegistration = cancellationToken.Register(() =>
            Fail(new(ProcessFailureKind.UserCancellation, "User cancellation requested.")));
        using var timeoutRegistration = timeout.Token.Register(() =>
            Fail(new(ProcessFailureKind.Timeout, "Process timeout elapsed.")));
        try
        {
            if (!process.Start())
                return StartFailed(new InvalidOperationException("Process.Start returned false."));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return StartFailed(exception);
        }

        ProcessResult StartFailed(Exception exception) => new(ProcessStatus.StartFailed, null,
            stdout.Snapshot(), stderr.Snapshot(), new(ProcessFailureKind.StartFailed, "Process could not start.", exception),
            true, true, true, null);

        Task readOutput = DrainAsync(process.StandardOutput.BaseStream, stdout, ProcessOutputStream.StandardOutput);
        Task readError = DrainAsync(process.StandardError.BaseStream, stderr, ProcessOutputStream.StandardError);
        Task waitForExit = WaitForRootAsync();
        Task finished = Task.WhenAll(waitForExit, readOutput, readError);

        await Task.WhenAny(waitForExit, failureSignal.Task).ConfigureAwait(false);
        var cleanupClock = Stopwatch.StartNew();
        ProcessFailure? cleanupFailure = null;
        if (!failureSignal.Task.IsCompleted)
        {
            // A naturally exited root can leave inherited pipe handles open in a child.
            Task winner = await Task.WhenAny(finished, failureSignal.Task, Task.Delay(CleanupTimeout)).ConfigureAwait(false);
            if (winner != finished && !failureSignal.Task.IsCompleted)
                Fail(new(ProcessFailureKind.CleanupFailed, "Output streams did not finish after root exit."));
        }
        if (failureSignal.Task.IsCompleted)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or AggregateException)
            {
                if (!RootHasExited())
                    cleanupFailure = new(ProcessFailureKind.CleanupFailed, "Process termination request failed.", exception);
            }
            TimeSpan remaining = CleanupTimeout - cleanupClock.Elapsed;
            Task winner = await Task.WhenAny(finished, Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)).ConfigureAwait(false);
            if (winner != finished)
            {
                cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Process or output cleanup exceeded 10 seconds.");
                stopReading.Cancel();
                process.StandardOutput.Dispose();
                process.StandardError.Dispose();
            }
        }

        bool rootExited = RootHasExited();
        var output = stdout.Snapshot();
        var errorOutput = stderr.Snapshot();
        // EOF and validity of a full capture are separate facts.
        bool pipesClosed = output.ReachedEndOfStream && errorOutput.ReachedEndOfStream;
        ProcessFailure? failure = failureSignal.Task.IsCompleted ? await failureSignal.Task.ConfigureAwait(false) : null;
        ProcessStatus status = failure is null ? ProcessStatus.Exited : ProcessStatus.ExecutionFailed;
        if (!rootExited || !pipesClosed || cleanupFailure is not null || failure?.Kind == ProcessFailureKind.CleanupFailed)
            status = ProcessStatus.TerminationFailed;
        if (failure?.Kind is ProcessFailureKind.UserCancellation or ProcessFailureKind.Timeout)
        {
            // Stage 2b must establish descendant-exit evidence before normal cancel/timeout results.
            status = ProcessStatus.TerminationFailed;
            cleanupFailure ??= new(ProcessFailureKind.CleanupFailed, "Descendant termination has not been verified.");
        }
        int? exitCode = rootExited ? process.ExitCode : null;
        return new(status, exitCode, output, errorOutput, failure, rootExited,
            pipesClosed, null, cleanupFailure);

        bool RootHasExited()
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        async Task WaitForRootAsync()
        {
            try { await process.WaitForExitAsync().ConfigureAwait(false); }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                Fail(new(ProcessFailureKind.ProcessWaitFailed, "Could not observe process exit.", exception));
            }
        }

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
                    int count = await stream.ReadAsync(buffer, stopReading.Token).ConfigureAwait(false);
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
                if (!callbackEnabled || count == 0) return;
                try
                {
                    for (int offset = 0; offset < count;)
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
}
