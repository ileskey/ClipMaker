using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class IndexedCollectedReference(OwnedMediaFile file, long bytes, long budgetUsed,
    string sha256, PresentationRange requested, PresentationRange actual) : CollectedSourceReference(file, bytes, budgetUsed, sha256)
{
    internal PresentationRange RequestedIndexRange { get; } = requested;
    internal PresentationRange ActualIndexRange { get; } = actual;
}

internal sealed record IndexedReferenceCollectionResult(OperationStatus Status, IndexedCollectedReference? Data,
    OperationError? Error, IReadOnlyList<OwnedMediaFile> RemainingFiles);

// The caller supplies the index and locator from the same confirmed prefix read.
// Index coordinates are not native or movie presentation coordinates.
internal sealed class IndexedReferenceCollector(BoundedSourceHttpReader reader)
{
    internal async Task<IndexedReferenceCollectionResult> CollectAsync(Guid fileId, Mp4SidxIndex index,
        PresentationRange requested, SelectedMediaLocator selected, long resourceLength, string workDirectory,
        SourceInspectionBudget budget, long alreadyReadBytes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(budget);
        if (fileId == Guid.Empty || !Path.IsPathFullyQualified(workDirectory)) throw new ArgumentException("File identity and absolute directory are required.");
        if (alreadyReadBytes < index.Initialization.Length || alreadyReadBytes > budget.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(alreadyReadBytes));
        var refs = index.References.Where(r => r.IndexRange.End.CompareTo(requested.Start) > 0 &&
            r.IndexRange.Start.CompareTo(requested.End) < 0).ToArray();
        if (refs.Length == 0 || refs.Length > 64 || index.Initialization.IsEmpty ||
            requested.Start.CompareTo(index.References[0].IndexRange.Start) < 0 ||
            requested.End.CompareTo(index.References[^1].IndexRange.End) > 0) return Fail(OperationErrorCode.UnsupportedFormat);
        PresentationRange.TryCreate(refs[0].IndexRange.Start, refs[^1].IndexRange.End, out var actual, out _);
        if ((actual!.End - actual.Start).CompareTo(budget.MaximumPresentationSpan) > 0) return Fail(OperationErrorCode.UnsupportedFormat);
        long planned = alreadyReadBytes;
        foreach (var r in refs)
        {
            if (r.Start < 0 || r.Bytes <= 0 || r.Bytes > SourceHttpRequest.MaximumReadBytes ||
                r.Start > resourceLength - r.Bytes || r.Bytes > budget.MaximumBytes - planned)
                return Fail(OperationErrorCode.UnsupportedFormat);
            planned += r.Bytes;
        }
        var headers = selected.Headers.ToArray();
        _ = new SourceHttpRequest(selected.Uri, SourceHttpMode.HeaderRange, 1, 0, 1, headers);
        if (token.IsCancellationRequested) return new(OperationStatus.Cancelled, null, null, []);
        if (!Directory.Exists(workDirectory)) return Fail(OperationErrorCode.OutputFailed);
        var file = new OwnedMediaFile(fileId, Path.Combine(Path.GetFullPath(workDirectory), $"indexed-ref-{fileId:N}.mp4"), [selected.Format.Id]);
        FileStream? output = null;
        bool owned = false, unsafeClose = false;
        var status = OperationStatus.Failed;
        OperationError? error = null;
        IndexedCollectedReference? data = null;
        try
        {
            token.ThrowIfCancellationRequested();
            output = new(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            owned = true;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await output.WriteAsync(index.Initialization, token).ConfigureAwait(false);
            hash.AppendData(index.Initialization.Span);
            long written = index.Initialization.Length, used = alreadyReadBytes;
            foreach (var r in refs)
            {
                var result = await reader.ReadAsync(new(selected.Uri, SourceHttpMode.HeaderRange,
                    (int)r.Bytes, r.Start, (int)r.Bytes, headers), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Status == OperationStatus.Cancelled) { status = OperationStatus.Cancelled; break; }
                if (result.Status != OperationStatus.Succeeded || result.ResourceLength != resourceLength ||
                    result.Bytes is not { } bytes || bytes.LongLength != r.Bytes)
                { error = Error(result.Failure == SourceHttpFailureKind.TransportFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed); break; }
                await output.WriteAsync(bytes, token).ConfigureAwait(false);
                hash.AppendData(bytes);
                used += bytes.Length;
                written += bytes.Length;
            }
            if (error is null && status != OperationStatus.Cancelled)
            {
                await output.FlushAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                var captured = new OwnedMediaFile(file.Id, file.Path, file.SourceFormatIds, new(written, digest));
                data = new(captured, written, used, digest, requested, actual);
                status = OperationStatus.Succeeded;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = OperationStatus.Cancelled; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { error = Error(!owned && File.Exists(file.Path) ? OperationErrorCode.OutputConflict : OperationErrorCode.OutputFailed); }
        finally
        {
            if (output is not null)
                try { await output.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    unsafeClose = true;
                    if (status == OperationStatus.Succeeded) { status = OperationStatus.Failed; error = Error(OperationErrorCode.OutputFailed); }
                }
        }
        if (status == OperationStatus.Succeeded && token.IsCancellationRequested) status = OperationStatus.Cancelled;
        if (status == OperationStatus.Succeeded) return new(status, data, null, []);
        var remaining = new List<OwnedMediaFile>();
        if (unsafeClose) remaining.Add(file);
        else if (owned)
            try { File.Delete(file.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { remaining.Add(file); }
        return new(status, null, status == OperationStatus.Cancelled ? null : error, remaining.AsReadOnly());
    }

    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "Indexed 원본 참조를 제한된 byte 범위와 파일 소유 조건으로 수집하지 못했습니다.");
    private static IndexedReferenceCollectionResult Fail(OperationErrorCode code) => new(OperationStatus.Failed, null, Error(code), []);
}
