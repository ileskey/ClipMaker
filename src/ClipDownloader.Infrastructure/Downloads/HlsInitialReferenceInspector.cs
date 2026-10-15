using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

// Resource handoff remains explicit even when the inspection failed or was cancelled.
internal sealed class HlsInitialReferenceInspectionResult(OperationStatus status, ProbedInitialReference? data,
    OperationError? error, IEnumerable<OwnedMediaFile>? remaining = null, HlsMediaPlaylist? playlist = null)
{
    internal OperationStatus Status { get; } = status;
    internal ProbedInitialReference? Data { get; } = data;
    internal OperationError? Error { get; } = error;
    internal IReadOnlyList<OwnedMediaFile> RemainingFiles { get; } = Array.AsReadOnly((remaining ?? []).ToArray());
    internal string? CleanupWarning => RemainingFiles.Count == 0 ? null : "초기 참조 파일의 소유 수명을 호출자가 계속 관리해야 합니다.";
    internal HlsMediaPlaylist? Playlist { get; } = playlist;
}

internal sealed class HlsInitialReferenceInspector
{
    private readonly BoundedSourceHttpReader http;
    private readonly HlsReferenceCollector collector;
    private readonly Func<HlsCollectedReference, CancellationToken, Task<OperationResult<ProbedInitialReference>>> probe;

    internal HlsInitialReferenceInspector(BoundedSourceHttpReader http, InitialReferenceProbe probe)
        : this(http, probe.ProbeAsync) { }

    // Trusted composition/test seam. Production uses the bound-file probe above.
    internal HlsInitialReferenceInspector(BoundedSourceHttpReader http,
        Func<HlsCollectedReference, CancellationToken, Task<OperationResult<ProbedInitialReference>>> probe,
        HlsReferenceCollector? collector = null)
    {
        ArgumentNullException.ThrowIfNull(http); ArgumentNullException.ThrowIfNull(probe);
        this.http = http; this.probe = probe; this.collector = collector ?? new(http);
    }

    internal async Task<HlsInitialReferenceInspectionResult> InspectAsync(SelectedSourceInspectionRequest request,
        SelectedSourceLocators locators, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(locators);
        if (token.IsCancellationRequested) return new(OperationStatus.Cancelled, null, null);
        if (!locators.Selection.Matches(request.Selection) || locators.Info.Source != request.Selection.Source ||
            locators.Info.State != VideoState.Completed || locators.Info.Duration != request.Selection.MetadataDuration ||
            locators.Video.Format.Id != request.Selection.Selection.VideoFormat.Id) return Fail(OperationErrorCode.VerificationFailed);
        if (locators.Video.Format.Transport != FormatTransport.Hls || locators.Audio is not null ||
            request.Selection.Selection.AudioFormat is not null) return Fail(OperationErrorCode.UnsupportedFormat);
        if (!Directory.Exists(request.WorkDirectory)) return Fail(OperationErrorCode.OutputFailed);
        int playlistLimit = (int)Math.Min(request.Budget.MaximumBytes, HlsMediaPlaylistParser.MaximumBytes);
        var playlist = await new HlsSelectedPlaylistReader(http).ReadAsync(locators.Video, playlistLimit, token).ConfigureAwait(false);
        if (token.IsCancellationRequested || playlist.Status == OperationStatus.Cancelled) return new(OperationStatus.Cancelled, null, null);
        if (playlist.Status != OperationStatus.Succeeded) return new(OperationStatus.Failed, null, playlist.Error);
        // First whole reference only. Playlist coordinate zero is not source/native origin.
        var first = playlist.Data!.Segments[0];
        PresentationRange.TryCreate(first.PlaylistRange.Start, first.PlaylistRange.End, out var range, out _);
        var plan = new HlsReferencePlanner().Plan(playlist.Data, range!, request.Budget.MaximumPresentationSpan, 1);
        if (plan.Status != OperationStatus.Succeeded) return new(OperationStatus.Failed, null, plan.Error);
        var collected = await collector.CollectAsync(Guid.NewGuid(), playlist.Data, plan.Data!, locators.Video,
            request.WorkDirectory, request.Budget, playlist.Data.SourceBytes, token).ConfigureAwait(false);
        if (collected.Status != OperationStatus.Succeeded)
            return new(collected.Status, null, collected.Error, collected.RemainingFiles);
        var owned = collected.Data!.File;
        // Never delete the file here: probe termination/remaining resources must be resolved by its owner.
        try
        {
            token.ThrowIfCancellationRequested();
            var result = await probe(collected.Data, token).ConfigureAwait(false);
            if (result.Status == OperationStatus.Failed) return new(OperationStatus.Failed, null, result.Error, [owned]);
            if (token.IsCancellationRequested || result.Status == OperationStatus.Cancelled)
                return new(OperationStatus.Cancelled, null, null, [owned]);
            if (!ReferenceEquals(result.Data!.Reference, collected.Data)) return Fail(OperationErrorCode.VerificationFailed, [owned]);
            return new(OperationStatus.Succeeded, result.Data, null, playlist: playlist.Data);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(OperationStatus.Cancelled, null, null, [owned]); }
    }

    private static HlsInitialReferenceInspectionResult Fail(OperationErrorCode code, IEnumerable<OwnedMediaFile>? remaining = null) =>
        new(OperationStatus.Failed, null, new(code, OperationErrorCategory.Execution,
            "선택 HLS 초기 참조 검사를 지원 범위에서 완료하지 못했습니다."), remaining);
}
