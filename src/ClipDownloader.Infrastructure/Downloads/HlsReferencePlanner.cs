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
            return Fail(OperationErrorCode.InvalidRange, "HlsPlaylistRange", "HLS 참조 요청이 playlist 시간 범위를 벗어났습니다.");
        var selected = playlist.Segments.Where(segment => segment.PlaylistRange.End.CompareTo(playlistRange.Start) > 0 &&
            segment.PlaylistRange.Start.CompareTo(playlistRange.End) < 0).ToArray();
        if (selected.Length == 0) return Fail(OperationErrorCode.VerificationFailed, "HlsSegmentCoverage", "HLS 참조 요청과 겹치는 세그먼트가 없습니다.");
        if (selected.Length > maximumSegments)
            return Fail(OperationErrorCode.UnsupportedFormat, "HlsSegmentCountBudget",
                FormattableString.Invariant($"HLS 획득에 필요한 세그먼트 {selected.Length}개가 상한 {maximumSegments}개를 초과합니다."));
        var fetchedSpan = selected[^1].PlaylistRange.End - selected[0].PlaylistRange.Start;
        if (fetchedSpan.CompareTo(maximumFetchedSpan) > 0)
            return Fail(OperationErrorCode.UnsupportedFormat, "HlsFetchedSpanBudget",
                $"HLS 세그먼트 전체 획득 범위 {Seconds(fetchedSpan)}초가 상한 {Seconds(maximumFetchedSpan)}초를 초과합니다. 획득 범위에는 요청 구간 외의 참조 여유 구간도 포함됩니다.");
        return OperationResult<HlsReferencePlan>.Succeeded(new(playlist.Id, playlist.Initialization, Array.AsReadOnly(selected), playlistRange));
    }
    private static string Seconds(SignedMediaTime time) => time.Denominator.IsOne
        ? FormattableString.Invariant($"{time.Numerator}")
        : FormattableString.Invariant($"{time.Numerator}/{time.Denominator}");

    private static OperationResult<HlsReferencePlan> Fail(OperationErrorCode code, string field, string message) =>
        OperationResult<HlsReferencePlan>.Failed(new(code, OperationErrorCategory.Execution, message, field));
}
