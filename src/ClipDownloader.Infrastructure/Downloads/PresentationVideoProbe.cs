using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class PresentationVideoProbe(IToolLocator locator, ToolPathSettings settings, IProcessRunner runner)
{
    internal Task<OperationResult<H264CarrierObservations>> ObserveCarrierAsync(AuxiliaryHlsCollectedReference reference, CancellationToken token) =>
        ExecuteAsync(reference, "ffprobe.exe", ToolKind.Ffprobe,
            ["-v", "error", "-protocol_whitelist", "file", "-format_whitelist", "mpegts", "-show_streams", "-show_packets", "-show_data",
                "-show_entries", "stream=index,codec_type,codec_name,time_base,pix_fmt,width,height:packet=stream_index,pts,size,data,flags", "-of", "json", "-i", reference.File.Path],
            bytes => new H264CarrierObservationParser().Parse(bytes), token);

    internal Task<OperationResult<DecodedPictureObservations>> DecodeAsync(CollectedSourceReference reference, CancellationToken token) =>
        ExecuteAsync(reference, "ffmpeg.exe", ToolKind.Ffmpeg,
            ["-v", "error", "-xerror", "-nostdin", "-protocol_whitelist", "file", "-format_whitelist",
                reference is AuxiliaryHlsCollectedReference ? "mpegts" : "mov", "-copyts", "-i", reference.File.Path,
                "-map", "0:v:0", "-an", "-c:v", "rawvideo", "-pix_fmt", "yuv420p", "-fps_mode:v", "passthrough",
                "-enc_time_base:v", "demux", "-f", "framehash", "-hash", "sha256", "-"],
            bytes => new DecodedPictureObservationParser().Parse(bytes), token);

    private async Task<OperationResult<T>> ExecuteAsync<T>(CollectedSourceReference reference, string executable, ToolKind kind,
        IReadOnlyList<string> args, Func<ReadOnlyMemory<byte>, OperationResult<T>> parse, CancellationToken token) where T : class
    {
        string stage = "FileIdentity";
        try
        {
            token.ThrowIfCancellationRequested();
            if (reference.Bytes <= 0 || reference.Sha256.Length != 64 || reference.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
                return Fail<T>(OperationErrorCode.VerificationFailed, stage);
            await using var file = new FileStream(reference.File.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous);
            if (!await Matches(file, reference, token).ConfigureAwait(false)) return Fail<T>(OperationErrorCode.VerificationFailed, stage);
            var location = locator.Locate(new(executable, settings.GetPath(executable)), token);
            if (location.Status != ToolLocationStatus.Found) return Fail<T>(OperationErrorCode.MissingTool, "PresentationVideoProbe");
            string directory = Path.GetDirectoryName(reference.File.Path)!;
            stage = "ProbeExecution";
            var version = await runner.RunAsync(new(location.SelectedPath!, ["-version"], directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(15)), null, token).ConfigureAwait(false);
            var failure = Check<T>(version);
            if (failure is not null) return failure;
            if (ToolOutputParser.ReadVersion(kind, new UTF8Encoding(false, true).GetString(version.StandardOutput.Bytes.Span)) is null)
                return Fail<T>(OperationErrorCode.ProcessFailed, stage);
            token.ThrowIfCancellationRequested();
            var result = await runner.RunAsync(new(location.SelectedPath!, args, directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(60)), null, token).ConfigureAwait(false);
            failure = Check<T>(result);
            if (failure is not null) return failure;
            if (result.StandardError.TotalBytes != 0) return Fail<T>(OperationErrorCode.VerificationFailed, "DecoderReportedError");
            token.ThrowIfCancellationRequested();
            stage = "FileIdentity";
            if (!await Matches(file, reference, token).ConfigureAwait(false)) return Fail<T>(OperationErrorCode.VerificationFailed, stage);
            return parse(result.StandardOutput.Bytes);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<T>.Cancelled(); }
        catch (DecoderFallbackException) { return Fail<T>(OperationErrorCode.ProcessFailed, "ProbeVersion"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Fail<T>(stage == "ProbeExecution" ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed, stage); }
    }
    private static OperationResult<T>? Check<T>(ProcessResult result) where T : class
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed || result.DescendantExitConfirmed != true || !result.StreamsDrained)
            return Fail<T>(OperationErrorCode.ProcessTerminationFailed, "PresentationVideoProbe");
        if (result.Status == ProcessStatus.Cancelled) return OperationResult<T>.Cancelled();
        if (result.Status == ProcessStatus.TimedOut) return Fail<T>(OperationErrorCode.TimedOut, "PresentationVideoProbe");
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0) return Fail<T>(OperationErrorCode.ProcessFailed, "PresentationVideoProbe");
        if (result.StandardOutput.LimitExceeded || !result.StandardOutput.IsComplete || !result.StandardOutput.ReachedEndOfStream || result.StandardOutput.IsTruncated)
            return Fail<T>(OperationErrorCode.InvalidMetadata, "PresentationVideoProbe");
        return null;
    }
    private static async Task<bool> Matches(FileStream file, CollectedSourceReference reference, CancellationToken token)
    {
        if (file.Length != reference.Bytes) return false;
        file.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static OperationResult<T> Fail<T>(OperationErrorCode code, string field) where T : class => OperationResult<T>.Failed(
        new(code, OperationErrorCategory.Execution, "원본과 참조의 local video 관찰을 완료하지 못했습니다.", field));
}
