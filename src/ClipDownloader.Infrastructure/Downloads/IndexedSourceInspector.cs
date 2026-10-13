using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class IndexedSourceIndex(SelectedMediaLocator locator, Mp4SidxIndex index, long resourceLength, int prefixBytes)
{
    internal SelectedMediaLocator Locator { get; } = locator;
    internal Mp4SidxIndex Index { get; } = index;
    internal long ResourceLength { get; } = resourceLength;
    internal int PrefixBytes { get; } = prefixBytes;
}

internal sealed record IndexedSourceInspectionResult(OperationStatus Status, ProbedInitialReference? Data,
    OperationError? Error, IndexedSourceIndex? Source, IReadOnlyList<OwnedMediaFile> RemainingFiles);

internal sealed class IndexedSourceInspector(BoundedSourceHttpReader reader,
    Func<CollectedSourceReference, CancellationToken, Task<OperationResult<ProbedInitialReference>>> probe)
{
    internal const int PrefixBytes = 65536;

    internal async Task<OperationResult<IndexedSourceIndex>> ReadIndexAsync(SelectedMediaLocator locator,
        SourceInspectionBudget budget, CancellationToken token)
    {
        if (budget.MaximumBytes < PrefixBytes) return Fail<IndexedSourceIndex>(OperationErrorCode.UnsupportedFormat);
        var prefix = await reader.ReadAsync(new(locator.Uri, SourceHttpMode.HeaderRange, PrefixBytes,
            0, PrefixBytes, locator.Headers), token).ConfigureAwait(false);
        if (prefix.Status == OperationStatus.Cancelled || token.IsCancellationRequested) return OperationResult<IndexedSourceIndex>.Cancelled();
        if (prefix.Status != OperationStatus.Succeeded || prefix.ResourceLength is not { } length || prefix.Bytes is not { } bytes)
            return Fail<IndexedSourceIndex>(prefix.Failure == SourceHttpFailureKind.TransportFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed);
        var index = new Mp4SidxReader().Read(bytes, length);
        if (index.Status != OperationStatus.Succeeded) return OperationResult<IndexedSourceIndex>.Failed(index.Error!);
        return OperationResult<IndexedSourceIndex>.Succeeded(new(locator, index.Data!, length, bytes.Length));
    }

    internal async Task<IndexedSourceInspectionResult> InspectInitialAsync(SelectedMediaLocator locator, string directory,
        SourceInspectionBudget budget, long alreadyReadBytes, CancellationToken token)
    {
        if (alreadyReadBytes < 0 || alreadyReadBytes > budget.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(alreadyReadBytes));
        if (budget.MaximumBytes - alreadyReadBytes < PrefixBytes) return new(OperationStatus.Failed, null,
            Error(OperationErrorCode.UnsupportedFormat), null, []);
        var source = await ReadIndexAsync(locator, budget, token).ConfigureAwait(false);
        if (source.Status != OperationStatus.Succeeded) return new(source.Status, null, source.Error, null, []);
        return await InspectAsync(source.Data!, source.Data!.Index.References[0].IndexRange, directory, budget,
            alreadyReadBytes + source.Data.PrefixBytes, token).ConfigureAwait(false);
    }

    internal async Task<IndexedSourceInspectionResult> InspectAsync(IndexedSourceIndex source, PresentationRange range,
        string directory, SourceInspectionBudget budget, long alreadyReadBytes, CancellationToken token)
    {
        var collected = await new IndexedReferenceCollector(reader).CollectAsync(Guid.NewGuid(), source.Index, range,
            source.Locator, source.ResourceLength, directory, budget, alreadyReadBytes, token).ConfigureAwait(false);
        if (collected.Status != OperationStatus.Succeeded)
            return new(collected.Status, null, collected.Error, source, collected.RemainingFiles);
        // Once a local probe starts the file remains owned even on failure/unconfirmed termination.
        var files = Array.AsReadOnly(new[] { collected.Data!.File });
        try
        {
            var observed = await probe(collected.Data, token).ConfigureAwait(false);
            if (observed.Status == OperationStatus.Succeeded && !ReferenceEquals(observed.Data!.Reference, collected.Data))
                return new(OperationStatus.Failed, null, Error(OperationErrorCode.VerificationFailed), source, files);
            if (token.IsCancellationRequested && observed.Error?.Code != OperationErrorCode.ProcessTerminationFailed)
                return new(OperationStatus.Cancelled, null, null, source, files);
            return new(observed.Status, observed.Data, observed.Error, source, files);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(OperationStatus.Cancelled, null, null, source, files); }
    }

    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "Indexed 원본의 제한된 초기 참조 검사를 완료하지 못했습니다.", "SourceIndex");
    private static OperationResult<T> Fail<T>(OperationErrorCode code) where T : class => OperationResult<T>.Failed(Error(code));
}
