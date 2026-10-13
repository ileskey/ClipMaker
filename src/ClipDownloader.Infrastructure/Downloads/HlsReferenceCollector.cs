using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class HlsCollectedReference(Guid playlistId, OwnedMediaFile file, long bytes, long budgetUsed,
    string sha256, PresentationRange requested, PresentationRange actual) : CollectedSourceReference(file, bytes, budgetUsed, sha256)
{
    internal Guid PlaylistId { get; } = playlistId;
    internal PresentationRange RequestedPlaylistRange { get; } = requested;
    internal PresentationRange ActualPlaylistRange { get; } = actual;
}

internal sealed class HlsReferenceCollectionResult
{
    internal OperationStatus Status { get; }
    internal HlsCollectedReference? Data { get; }
    internal OperationError? Error { get; }
    internal IReadOnlyList<OwnedMediaFile> RemainingFiles { get; }
    internal string? CleanupWarning => RemainingFiles.Count == 0 ? null : "이번 수집에서 만든 임시 파일을 정리하지 못했습니다.";
    internal HlsReferenceCollectionResult(OperationStatus status, HlsCollectedReference? data, OperationError? error,
        IEnumerable<OwnedMediaFile>? remaining = null)
    {
        Status = status; Data = data; Error = error;
        RemainingFiles = Array.AsReadOnly((remaining ?? []).ToArray());
    }
}

internal sealed class HlsReferenceCollector
{
    private readonly BoundedSourceHttpReader reader;
    private readonly Func<string, Stream> createNewFile;
    private readonly Action<string> deleteOwnedFile;

    // Storage delegates are trusted test/composition dependencies. Production always uses CreateNew.
    internal HlsReferenceCollector(BoundedSourceHttpReader reader, Func<string, Stream>? createNewFile = null,
        Action<string>? deleteOwnedFile = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
        this.createNewFile = createNewFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous));
        this.deleteOwnedFile = deleteOwnedFile ?? File.Delete;
    }

    internal async Task<HlsReferenceCollectionResult> CollectAsync(Guid fileId, HlsMediaPlaylist playlist,
        HlsReferencePlan plan, SelectedMediaLocator selected, string workDirectory, SourceInspectionBudget budget,
        long alreadyReadBytes, CancellationToken token)
    {
        if (fileId == Guid.Empty) throw new ArgumentException("File identity is required.", nameof(fileId));
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(budget);
        if (alreadyReadBytes < playlist.SourceBytes || alreadyReadBytes > budget.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(alreadyReadBytes));
        if (!Path.IsPathFullyQualified(workDirectory)) throw new ArgumentException("An absolute work directory is required.", nameof(workDirectory));
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workDirectory));
        var segments = plan.Segments.ToArray();
        if (playlist.Uri != selected.Uri || plan.PlaylistId != playlist.Id || plan.Initialization != playlist.Initialization ||
            segments.Length == 0 || selected.Format.Transport != FormatTransport.Hls)
            return Failed(OperationErrorCode.VerificationFailed);
        // Recompute membership from this immutable playlist instead of trusting a foreign or edited plan.
        var expected = playlist.Segments.Where(segment => segment.PlaylistRange.End.CompareTo(plan.RequestedPlaylistRange.Start) > 0 &&
            segment.PlaylistRange.Start.CompareTo(plan.RequestedPlaylistRange.End) < 0).ToArray();
        if (plan.RequestedPlaylistRange.Start.Numerator < 0 || plan.RequestedPlaylistRange.End.CompareTo(playlist.Duration) > 0 ||
            !segments.SequenceEqual(expected)) return Failed(OperationErrorCode.VerificationFailed);
        PresentationRange.TryCreate(segments[0].PlaylistRange.Start, segments[^1].PlaylistRange.End, out var actual, out _);
        if ((actual!.End - actual.Start).CompareTo(budget.MaximumPresentationSpan) > 0) return Failed(OperationErrorCode.UnsupportedFormat);
        if (token.IsCancellationRequested) return new(OperationStatus.Cancelled, null, null);
        if (!Directory.Exists(directory)) return Failed(OperationErrorCode.OutputFailed);
        var file = new OwnedMediaFile(fileId, Path.Combine(directory, $"hls-ref-{fileId:N}.mp4"), [selected.Format.Id]);
        var headers = selected.Headers.ToArray();
        _ = new SourceHttpRequest(plan.Initialization, SourceHttpMode.Resource, 1, headers: headers);
        long used = alreadyReadBytes, written = 0;
        bool owned = false;
        bool cleanupUnsafe = false;
        HlsCollectedReference? completed = null;
        OperationError? error = null;
        OperationStatus status = OperationStatus.Failed;
        Stream? output = null;
        try
        {
            token.ThrowIfCancellationRequested();
            output = createNewFile(file.Path);
            owned = true;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var uri in new[] { plan.Initialization }.Concat(segments.Select(segment => segment.Uri)))
            {
                token.ThrowIfCancellationRequested();
                long remaining = budget.MaximumBytes - used;
                if (remaining <= 0) { error = Error(OperationErrorCode.UnsupportedFormat); break; }
                int cap = (int)Math.Min(remaining, SourceHttpRequest.MaximumReadBytes);
                var result = await reader.ReadAsync(new(uri, SourceHttpMode.Resource, cap, headers: headers), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Status == OperationStatus.Cancelled) { status = OperationStatus.Cancelled; break; }
                if (result.Status == OperationStatus.Failed)
                {
                    error = Error(result.Failure switch
                    {
                        SourceHttpFailureKind.SizeLimitExceeded => OperationErrorCode.UnsupportedFormat,
                        SourceHttpFailureKind.TransportFailed => OperationErrorCode.ProcessFailed,
                        _ => OperationErrorCode.VerificationFailed
                    });
                    break;
                }
                if (result.Bytes is not { Length: > 0 } bytes) { error = Error(OperationErrorCode.VerificationFailed); break; }
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                hash.AppendData(bytes);
                used += bytes.Length;
                written += bytes.Length;
            }
            if (error is null && status != OperationStatus.Cancelled)
            {
                await output.FlushAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                completed = new(playlist.Id, file, written, used,
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), plan.RequestedPlaylistRange, actual);
                status = OperationStatus.Succeeded;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = OperationStatus.Cancelled; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = Error(!owned && File.Exists(file.Path) ? OperationErrorCode.OutputConflict : OperationErrorCode.OutputFailed);
            status = OperationStatus.Failed;
        }
        finally
        {
            if (output is not null)
            {
                try { await output.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // An uncertain handle lifetime must not lead to deleting a possibly in-use file.
                    if (status == OperationStatus.Succeeded || status != OperationStatus.Cancelled && error is null)
                    {
                        status = OperationStatus.Failed;
                        error = Error(OperationErrorCode.OutputFailed);
                    }
                    completed = null;
                    cleanupUnsafe = true;
                }
            }
        }
        if (status == OperationStatus.Succeeded && token.IsCancellationRequested) status = OperationStatus.Cancelled;
        if (status == OperationStatus.Succeeded) return new(status, completed, null);
        completed = null;
        var remainingFiles = new List<OwnedMediaFile>();
        // A failed dispose may leave the file in use; retain it rather than attempting deletion.
        if (cleanupUnsafe) remainingFiles.Add(file);
        else if (owned)
        {
            try { deleteOwnedFile(file.Path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { remainingFiles.Add(file); }
        }
        return new(status, null, status == OperationStatus.Cancelled ? null : error, remainingFiles);
    }

    private static HlsReferenceCollectionResult Failed(OperationErrorCode code) => new(OperationStatus.Failed, null, Error(code));
    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "HLS 원본 참조를 검사 budget/파일 소유/완전성 조건으로 수집하지 못했습니다.");
}
