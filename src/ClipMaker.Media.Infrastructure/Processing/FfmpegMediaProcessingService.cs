using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipMaker.Media.Infrastructure.Processing;

public sealed class FfmpegMediaProcessingService(IToolLocator locator, ToolPathSettings settings, IProcessRunner runner) : IMediaProcessingService
{
    private sealed class Job
    {
        internal MediaOwnedProcessingFile? File;
        internal bool Unconfirmed;
        internal bool NeedsCleanup;
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Job> jobs = [];
    private readonly SemaphoreSlim operations = new(1, 1);

    public MediaProcessingResources GetOwnedResources(Guid operationId)
    {
        lock (gate)
            return jobs.TryGetValue(operationId, out var job) ? new(job.File is null ? [] : [job.File], job.Unconfirmed, job.NeedsCleanup) : new([], false, false);
    }

    public async Task<MediaOperationResult<MediaProcessingResult>> ProcessAsync(MediaProcessingRequest request,
        IProgress<MediaProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested) return MediaOperationResult<MediaProcessingResult>.Cancelled();
        try { await operations.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return MediaOperationResult<MediaProcessingResult>.Cancelled(); }
        var inputs = new List<FileStream>();
        FileStream? output = null;
        Job? job = null;
        MediaOperationResult<MediaProcessingResult>? outcome = null;
        string stage = "InputOpen";
        try
        {
            lock (gate)
            {
                if (jobs.ContainsKey(request.OperationId)) return Fail(MediaFailureKind.OutputFailed, "ProcessingOperationReuse");
                if (jobs.Values.Any(j => j.Unconfirmed)) return Fail(MediaFailureKind.TerminationUnconfirmed, "PreviousProcessingTermination");
                job = new(); jobs.Add(request.OperationId, job);
            }
            var bound = new FastCopyCommandBinder().Bind(request);
            if (bound.Status != MediaOperationStatus.Succeeded) return MediaOperationResult<MediaProcessingResult>.Failed(bound.Failure!);
            string directory = Path.GetDirectoryName(request.StagingPath)!;
            if (!Directory.Exists(directory)) return Fail(MediaFailureKind.OutputFailed, "OutputDirectory");
            foreach (var file in request.Plan.Inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                inputs.Add(input);
                if (!await Matches(input, file.Content, cancellationToken).ConfigureAwait(false))
                    return Fail(MediaFailureKind.InvalidMedia, "InputByteIdentityBefore");
            }
            var location = locator.Locate(new("ffmpeg.exe", settings.GetPath("ffmpeg.exe")), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (location.Status != ToolLocationStatus.Found) return Fail(MediaFailureKind.MissingTool, "Ffmpeg");
            stage = "VersionExecution";
            var version = await runner.RunAsync(new(location.SelectedPath!, ["-version"], directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(15)), null, cancellationToken).ConfigureAwait(false);
            outcome = Check(version, job, requireFullOutput: true);
            if (outcome is not null) return outcome;
            cancellationToken.ThrowIfCancellationRequested();
            string text = new UTF8Encoding(false, true).GetString(version.StandardOutput.Bytes.Span);
            var match = Regex.Match(text, @"\Affmpeg version ([A-Za-z0-9][A-Za-z0-9._+\-]{0,127})(?:\s|$)", RegexOptions.CultureInvariant);
            if (!match.Success) return Fail(MediaFailureKind.ProcessFailed, "FfmpegVersion");
            stage = "OutputCreate";
            // Keep a no-delete reader continuously across closing the CreateNew
            // writer and the tool run. FFmpeg may write only this reserved file;
            // -y never applies to an unrelated pre-existing staging path.
            await using (var reservation = new FileStream(request.StagingPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite, 81920, FileOptions.Asynchronous))
            {
                lock (gate) job.File = new(request.OutputFileId, request.StagingPath, null);
                output = new(request.StagingPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
            }
            progress?.Report(new(null, "검증한 native 계획으로 copy/mux를 실행합니다."));
            cancellationToken.ThrowIfCancellationRequested();
            stage = "ProcessingExecution";
            var result = await runner.RunAsync(new(location.SelectedPath!, bound.Data!.Arguments, directory,
                ProcessOutputMode.Drain, ProcessOutputMode.Tail), null, cancellationToken).ConfigureAwait(false);
            outcome = Check(result, job);
            if (outcome is not null) return outcome;
            if (result.StandardError.TotalBytes != 0) return Fail(MediaFailureKind.ProcessFailed, "ProcessingReportedError");
            cancellationToken.ThrowIfCancellationRequested();
            stage = "InputRead";
            for (int i = 0; i < inputs.Count; i++)
                if (!await Matches(inputs[i], request.Plan.Inputs[i].Content, cancellationToken).ConfigureAwait(false))
                    return Fail(MediaFailureKind.InvalidMedia, "InputByteIdentityAfter");
            stage = "OutputRead";
            if (output.Length <= 0) return Fail(MediaFailureKind.OutputFailed, "EmptyStaging");
            long size = output.Length; output.Position = 0;
            var digest = await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false);
            if (output.Length != size) return Fail(MediaFailureKind.OutputFailed, "StagingChanged");
            var captured = new MediaOwnedProcessingFile(request.OutputFileId, request.StagingPath,
                new(size, Convert.ToHexString(digest)));
            lock (gate) job.File = captured;
            progress?.Report(new(100, "Staging copy/mux를 완료했습니다. 출력 의미 검증이 필요합니다."));
            cancellationToken.ThrowIfCancellationRequested();
            outcome = MediaOperationResult<MediaProcessingResult>.Succeeded(new(request, captured, match.Groups[1].Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = MediaOperationResult<MediaProcessingResult>.Cancelled(); }
        catch (DecoderFallbackException) { outcome = Fail(MediaFailureKind.ProcessFailed, "FfmpegVersion"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            outcome = Fail(stage == "OutputCreate" || stage == "OutputRead" ? MediaFailureKind.OutputFailed :
                stage.EndsWith("Execution", StringComparison.Ordinal) ? MediaFailureKind.ProcessFailed : MediaFailureKind.InvalidMedia,
                stage == "OutputCreate" && job?.File is null && File.Exists(request.StagingPath) ? "OutputConflict" : stage);
        }
        finally
        {
            bool closeFailed = false;
            foreach (var file in inputs.Concat(output is null ? [] : new[] { output }))
                try { await file.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { closeFailed = true; }
            if (closeFailed && outcome?.Status == MediaOperationStatus.Succeeded) outcome = Fail(MediaFailureKind.OutputFailed, "ProcessingFileClose");
            lock (gate)
                if (job is not null) job.NeedsCleanup = job.File is not null;
            operations.Release();
        }
        if (outcome?.Status == MediaOperationStatus.Succeeded && cancellationToken.IsCancellationRequested)
            return MediaOperationResult<MediaProcessingResult>.Cancelled();
        return outcome ?? Fail(MediaFailureKind.ProcessFailed, stage);
    }

    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity identity, CancellationToken token)
    {
        if (input.Length != identity.Bytes) return false;
        input.Position = 0;
        var digest = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return input.Length == identity.Bytes && Convert.ToHexString(digest).Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private MediaOperationResult<MediaProcessingResult>? Check(ProcessResult result, Job job, bool requireFullOutput = false)
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed || result.DescendantExitConfirmed != true || !result.StreamsDrained)
        {
            lock (gate) job.Unconfirmed = true;
            return Fail(MediaFailureKind.TerminationUnconfirmed, "ProcessingTermination");
        }
        if (result.Status == ProcessStatus.Cancelled) return MediaOperationResult<MediaProcessingResult>.Cancelled();
        if (result.Status == ProcessStatus.TimedOut) return Fail(MediaFailureKind.TimedOut, "ProcessingTimeout");
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0 || result.StandardOutput.LimitExceeded ||
            !result.StandardOutput.ReachedEndOfStream || !result.StandardOutput.IsComplete ||
            requireFullOutput && (result.StandardOutput.IsTruncated || result.StandardOutput.TotalBytes != result.StandardOutput.Bytes.Length))
            return Fail(MediaFailureKind.ProcessFailed, "ProcessingExecution");
        return null;
    }
    private static MediaOperationResult<MediaProcessingResult> Fail(MediaFailureKind kind, string field) =>
        MediaOperationResult<MediaProcessingResult>.Failed(new(kind, "미디어 처리 또는 처리 자원의 완전성을 확인하지 못했습니다.", field));
}
