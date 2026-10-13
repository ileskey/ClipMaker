using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record ProbedInitialReference(HlsCollectedReference Reference,
    string ProbeVersion, InitialReferenceObservations Observations);

internal sealed class InitialReferenceProbe(IToolLocator locator, ToolPathSettings settings, IProcessRunner runner)
{
    internal async Task<OperationResult<ProbedInitialReference>> ProbeAsync(HlsCollectedReference reference, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(reference);
        try
        {
            token.ThrowIfCancellationRequested();
            if (reference.Bytes <= 0 || reference.BudgetUsed < reference.Bytes || reference.Sha256.Length != 64 ||
                !reference.Sha256.All(char.IsAsciiHexDigit)) return Failed(OperationErrorCode.VerificationFailed);
            // Hold a read-only share through both hashes and the probe. On Windows this prevents writes/deletion.
            await using var file = new FileStream(reference.File.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (!await Matches(file, reference, token).ConfigureAwait(false)) return Failed(OperationErrorCode.VerificationFailed);
            var location = locator.Locate(new("ffprobe.exe", settings.GetPath("ffprobe.exe")), token);
            if (location.Status != ToolLocationStatus.Found) return Failed(OperationErrorCode.MissingTool);
            string directory = Path.GetDirectoryName(reference.File.Path)!;
            var versionResult = await runner.RunAsync(new(location.SelectedPath!, ["-version"], directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(15)), null, token).ConfigureAwait(false);
            var failure = Check(versionResult);
            if (failure is not null) return failure;
            token.ThrowIfCancellationRequested();
            string? version = ToolOutputParser.ReadVersion(ToolKind.Ffprobe,
                new UTF8Encoding(false, true).GetString(versionResult.StandardOutput.Bytes.Span));
            if (version is null) return Failed(OperationErrorCode.ProcessFailed);
            var probeResult = await runner.RunAsync(new(location.SelectedPath!,
                ["-v", "error", "-protocol_whitelist", "file", "-format_whitelist", "mov", "-show_streams", "-show_frames",
                    "-show_entries", "stream=index,codec_type,time_base,start_pts,r_frame_rate,avg_frame_rate:frame=stream_index,media_type,pts,duration,nb_samples",
                    "-of", "json", "-i", reference.File.Path], directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(60)), null, token).ConfigureAwait(false);
            failure = Check(probeResult);
            if (failure is not null) return failure;
            token.ThrowIfCancellationRequested();
            if (!await Matches(file, reference, token).ConfigureAwait(false)) return Failed(OperationErrorCode.VerificationFailed);
            var parsed = new InitialReferenceObservationParser().Parse(probeResult.StandardOutput.Bytes);
            token.ThrowIfCancellationRequested();
            return parsed.Status == OperationStatus.Succeeded
                ? OperationResult<ProbedInitialReference>.Succeeded(new(reference, version, parsed.Data!))
                : OperationResult<ProbedInitialReference>.Failed(parsed.Error!);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<ProbedInitialReference>.Cancelled(); }
        catch (DecoderFallbackException) { return Failed(OperationErrorCode.ProcessFailed); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Failed(OperationErrorCode.VerificationFailed); }
    }

    private static async Task<bool> Matches(FileStream file, HlsCollectedReference reference, CancellationToken token)
    {
        if (file.Length != reference.Bytes) return false;
        file.Position = 0;
        var digest = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
        return file.Length == reference.Bytes && Convert.ToHexString(digest).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static OperationResult<ProbedInitialReference>? Check(ProcessResult result)
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed ||
            result.DescendantExitConfirmed != true || !result.StreamsDrained) return Failed(OperationErrorCode.ProcessTerminationFailed);
        if (result.Status == ProcessStatus.Cancelled) return OperationResult<ProbedInitialReference>.Cancelled();
        if (result.Status == ProcessStatus.TimedOut) return Failed(OperationErrorCode.TimedOut);
        if (result.StandardOutput.LimitExceeded) return Failed(OperationErrorCode.InvalidMetadata);
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0) return Failed(OperationErrorCode.ProcessFailed);
        if (!result.StandardOutput.IsComplete || !result.StandardOutput.ReachedEndOfStream || result.StandardOutput.IsTruncated)
            return Failed(OperationErrorCode.InvalidMetadata);
        return null;
    }

    private static OperationResult<ProbedInitialReference> Failed(OperationErrorCode code) =>
        OperationResult<ProbedInitialReference>.Failed(new(code, OperationErrorCategory.Execution, code switch
        {
            OperationErrorCode.MissingTool => "초기 참조 검사에 필요한 FFprobe를 찾지 못했습니다.",
            OperationErrorCode.VerificationFailed => "수집한 초기 참조 파일의 byte identity를 확인하지 못했습니다.",
            OperationErrorCode.ProcessTerminationFailed => "초기 참조 검사 프로세스 또는 스트림의 종료를 확인하지 못했습니다.",
            OperationErrorCode.TimedOut => "초기 참조 검사 제한 시간을 초과했습니다.",
            OperationErrorCode.InvalidMetadata => "초기 참조 관찰 출력이 불완전하거나 상한을 초과했습니다.",
            _ => "초기 참조 검사 도구를 정상적으로 실행하지 못했습니다."
        }));
}
