using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class HlsSelectedPlaylistReader(BoundedSourceHttpReader reader)
{
    internal async Task<OperationResult<HlsMediaPlaylist>> ReadAsync(SelectedMediaLocator selected, int maximumBytes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (maximumBytes <= 0 || maximumBytes > HlsMediaPlaylistParser.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (selected.Format.Transport != FormatTransport.Hls) return Fail(OperationErrorCode.UnsupportedFormat);
        var result = await reader.ReadAsync(new(selected.Uri, SourceHttpMode.Resource, maximumBytes, headers: selected.Headers), token).ConfigureAwait(false);
        if (result.Status == OperationStatus.Cancelled || token.IsCancellationRequested) return OperationResult<HlsMediaPlaylist>.Cancelled();
        if (result.Status != OperationStatus.Succeeded) return Fail(result.Failure == SourceHttpFailureKind.TransportFailed
            ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed);
        return new HlsMediaPlaylistParser().Parse(result.Bytes!, selected.Uri);
    }
    private static OperationResult<HlsMediaPlaylist> Fail(OperationErrorCode code) =>
        OperationResult<HlsMediaPlaylist>.Failed(new(code, OperationErrorCategory.Execution, "선택 HLS playlist를 완전히 조사하지 못했습니다."));
}
