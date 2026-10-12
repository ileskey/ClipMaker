using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

// Only playlist-relative EXTINF coordinates; no user/source/native clock mapping is implied.
internal sealed class HlsReferencePlan(Guid playlistId, Uri initialization, IReadOnlyList<HlsSegment> segments, PresentationRange requested)
{
    internal Guid PlaylistId { get; } = playlistId;
    internal Uri Initialization { get; } = initialization;
    internal IReadOnlyList<HlsSegment> Segments { get; } = segments;
    internal PresentationRange RequestedPlaylistRange { get; } = requested;
}

internal sealed class HlsReferencePlanner
{
    internal OperationResult<HlsReferencePlan> Plan(HlsMediaPlaylist playlist, PresentationRange playlistRange,
        SignedMediaTime maximumFetchedSpan, int maximumSegments)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(playlistRange);
        ArgumentNullException.ThrowIfNull(maximumFetchedSpan);
        if (maximumFetchedSpan.Numerator <= 0 || maximumSegments <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFetchedSpan));
        if (playlistRange.Start.Numerator < 0 || playlistRange.End.CompareTo(playlist.Duration) > 0)
            return Fail(OperationErrorCode.InvalidRange);
        var selected = playlist.Segments.Where(segment => segment.PlaylistRange.End.CompareTo(playlistRange.Start) > 0 &&
            segment.PlaylistRange.Start.CompareTo(playlistRange.End) < 0).ToArray();
        if (selected.Length == 0) return Fail(OperationErrorCode.VerificationFailed);
        if (selected.Length > maximumSegments ||
            (selected[^1].PlaylistRange.End - selected[0].PlaylistRange.Start).CompareTo(maximumFetchedSpan) > 0)
            return Fail(OperationErrorCode.UnsupportedFormat);
        return OperationResult<HlsReferencePlan>.Succeeded(new(playlist.Id, playlist.Initialization, Array.AsReadOnly(selected), playlistRange));
    }
    private static OperationResult<HlsReferencePlan> Fail(OperationErrorCode code) =>
        OperationResult<HlsReferencePlan>.Failed(new(code, OperationErrorCategory.Execution, "HLS 참조 구간이 playlist/지원 검사 범위를 벗어났습니다."));
}
