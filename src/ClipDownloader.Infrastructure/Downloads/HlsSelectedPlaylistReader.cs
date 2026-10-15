using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class HlsSelectedPlaylistReader(BoundedSourceHttpReader reader)
{
    internal async Task<OperationResult<HlsMediaPlaylist>> ReadAsync(SelectedMediaLocator selected, int maximumBytes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (maximumBytes <= 0 || maximumBytes > HlsMediaPlaylistParser.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (selected.Format.Transport != FormatTransport.Hls) return Fail(OperationErrorCode.UnsupportedFormat, "HlsPlaylistTransportUnsupported");
        var result = await reader.ReadAsync(new(selected.Uri, SourceHttpMode.Resource, maximumBytes, headers: selected.Headers), token).ConfigureAwait(false);
        if (result.Status == OperationStatus.Cancelled || token.IsCancellationRequested) return OperationResult<HlsMediaPlaylist>.Cancelled();
        if (result.Status != OperationStatus.Succeeded)
        {
            if (result.Failure == SourceHttpFailureKind.SizeLimitExceeded)
                return OperationResult<HlsMediaPlaylist>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
                    $"선택 HLS playlist가 byte 검사 상한 {maximumBytes} bytes를 초과했습니다.", "HlsPlaylistByteBudget"));
            return Fail(result.Failure == SourceHttpFailureKind.TransportFailed
                ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed,
                result.Failure == SourceHttpFailureKind.TransportFailed ? "HlsPlaylistHttpTransport" : "HlsPlaylistHttpResponse");
        }
        return new HlsMediaPlaylistParser().Parse(result.Bytes!, selected.Uri);
    }
    private static OperationResult<HlsMediaPlaylist> Fail(OperationErrorCode code, string field) =>
        OperationResult<HlsMediaPlaylist>.Failed(new(code, OperationErrorCategory.Execution, "선택 HLS playlist를 완전히 조사하지 못했습니다.", field));
}
