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
internal sealed class IndexedReferenceCollector
{
    private readonly BoundedSourceHttpReader reader;
    private readonly Func<string, Stream> createNewFile;
    private readonly Action<string> deleteOwnedFile;

    // Trusted storage dependencies; production creates a new file and never overwrites.
    internal IndexedReferenceCollector(BoundedSourceHttpReader reader, Func<string, Stream>? createNewFile = null,
        Action<string>? deleteOwnedFile = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        this.reader = reader;
        this.createNewFile = createNewFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous));
        this.deleteOwnedFile = deleteOwnedFile ?? File.Delete;
    }

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
        if (refs.Length == 0 || index.Initialization.IsEmpty ||
            requested.Start.CompareTo(index.References[0].IndexRange.Start) < 0 ||
            requested.End.CompareTo(index.References[^1].IndexRange.End) > 0)
            return Fail(OperationErrorCode.UnsupportedFormat, "IndexedReferenceCoverage", "Indexed 요청의 참조 범위/초기화 조건을 확인하지 못했습니다.");
        if (refs.Length > 64) return Fail(OperationErrorCode.UnsupportedFormat, "IndexedReferenceCountBudget",
            FormattableString.Invariant($"Indexed 획득에 필요한 참조 {refs.Length}개가 상한 64개를 초과합니다."));
        PresentationRange.TryCreate(refs[0].IndexRange.Start, refs[^1].IndexRange.End, out var actual, out _);
        var fetchedSpan = actual!.End - actual.Start;
        if (fetchedSpan.CompareTo(budget.MaximumPresentationSpan) > 0)
            return Fail(OperationErrorCode.UnsupportedFormat, "IndexedFetchedSpanBudget",
                $"Indexed 참조 전체 획득 범위 {Seconds(fetchedSpan)}초가 상한 {Seconds(budget.MaximumPresentationSpan)}초를 초과합니다. 획득 범위에는 요청 구간 외의 참조 여유 구간도 포함됩니다.");
        long planned = alreadyReadBytes;
        foreach (var r in refs)
        {
            if (r.Start < 0 || r.Bytes <= 0 || r.Start > resourceLength - r.Bytes)
                return Fail(OperationErrorCode.UnsupportedFormat, "IndexedByteRange", "Indexed 참조의 원본 바이트 범위를 확인하지 못했습니다.");
            if (r.Bytes > SourceHttpRequest.MaximumReadBytes)
                return Fail(OperationErrorCode.UnsupportedFormat, "IndexedReferenceByteLimit", "Indexed 단일 참조가 HTTP 읽기 바이트 상한을 초과합니다.");
            if (r.Bytes > budget.MaximumBytes - planned)
                return Fail(OperationErrorCode.UnsupportedFormat, "SourceByteBudget",
                    FormattableString.Invariant($"Indexed 참조의 누적 획득 비용이 바이트 상한 {budget.MaximumBytes} bytes를 초과합니다."));
            planned += r.Bytes;
        }
        var headers = selected.Headers.ToArray();
        _ = new SourceHttpRequest(selected.Uri, SourceHttpMode.HeaderRange, 1, 0, 1, headers);
        if (token.IsCancellationRequested) return new(OperationStatus.Cancelled, null, null, []);
        if (!Directory.Exists(workDirectory)) return Fail(OperationErrorCode.OutputFailed);
        var file = new OwnedMediaFile(fileId, Path.Combine(Path.GetFullPath(workDirectory), $"indexed-ref-{fileId:N}.mp4"), [selected.Format.Id]);
        Stream? output = null;
        bool owned = false, unsafeClose = false;
        var status = OperationStatus.Failed;
        OperationError? error = null;
        IndexedCollectedReference? data = null;
        try
        {
            token.ThrowIfCancellationRequested();
            output = createNewFile(file.Path);
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
        if (unsafeClose) remaining.Add(new(file.Id, file.Path, file.SourceFormatIds, mayBeInUse: true));
        else if (owned)
            try { deleteOwnedFile(file.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { remaining.Add(file); }
        return new(status, null, status == OperationStatus.Cancelled ? null : error, remaining.AsReadOnly());
    }

    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "Indexed 원본 참조를 제한된 byte 범위와 파일 소유 조건으로 수집하지 못했습니다.");
    private static IndexedReferenceCollectionResult Fail(OperationErrorCode code) => new(OperationStatus.Failed, null, Error(code), []);
    private static IndexedReferenceCollectionResult Fail(OperationErrorCode code, string field, string message) =>
        new(OperationStatus.Failed, null, new(code, OperationErrorCategory.Execution, message, field), []);
    private static string Seconds(SignedMediaTime time) => time.Denominator.IsOne
        ? FormattableString.Invariant($"{time.Numerator}")
        : FormattableString.Invariant($"{time.Numerator}/{time.Denominator}");
}
