using System.Security.Cryptography;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class AuxiliaryHlsCollectedReference(OwnedMediaFile file, long bytes, long budgetUsed, string sha256,
    HlsSegment segment) : CollectedSourceReference(file, bytes, budgetUsed, sha256)
{
    internal HlsSegment Segment { get; } = segment;
}
internal sealed record AuxiliaryHlsCollectionResult(OperationStatus Status, AuxiliaryHlsCollectedReference? Data,
    OperationError? Error, IReadOnlyList<OwnedMediaFile> RemainingFiles);

internal sealed class AuxiliaryHlsReferenceCollector(BoundedSourceHttpReader reader)
{
    internal async Task<AuxiliaryHlsCollectionResult> CollectFirstAsync(SelectedMediaLocator selected,
        string directory, SourceInspectionBudget budget, long alreadyReadBytes, CancellationToken token)
    {
        if (alreadyReadBytes < 0 || alreadyReadBytes > budget.MaximumBytes) throw new ArgumentOutOfRangeException(nameof(alreadyReadBytes));
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute directory is required.", nameof(directory));
        if (selected.Format.Transport != FormatTransport.Hls) return Fail(OperationErrorCode.UnsupportedFormat);
        if (!Directory.Exists(directory)) return Fail(OperationErrorCode.OutputFailed);
        long remaining = budget.MaximumBytes - alreadyReadBytes;
        if (remaining <= 0) return Fail(OperationErrorCode.UnsupportedFormat);
        var leaf = await reader.ReadAsync(new(selected.Uri, SourceHttpMode.Resource, (int)Math.Min(remaining, AuxiliaryHlsPlaylistParser.MaximumBytes),
            headers: selected.Headers), token).ConfigureAwait(false);
        if (leaf.Status != OperationStatus.Succeeded) return FromHttp(leaf);
        var parsed = new AuxiliaryHlsPlaylistParser().Parse(leaf.Bytes!, selected.Uri);
        if (parsed.Status != OperationStatus.Succeeded) return new(parsed.Status, null, parsed.Error, []);
        var first = parsed.Data!.Segments[0];
        if (first.Sequence != 0 || first.PlaylistRange.End.CompareTo(budget.MaximumPresentationSpan) > 0)
            return Fail(OperationErrorCode.UnsupportedFormat);
        long used = alreadyReadBytes + leaf.Bytes!.Length;
        remaining = budget.MaximumBytes - used;
        if (remaining <= 0) return Fail(OperationErrorCode.UnsupportedFormat);
        var body = await reader.ReadAsync(new(first.Uri, SourceHttpMode.Resource, (int)Math.Min(remaining, 4 * 1024 * 1024),
            headers: selected.Headers), token).ConfigureAwait(false);
        if (body.Status != OperationStatus.Succeeded) return FromHttp(body);
        if (body.Bytes is not { Length: > 0 } bytes) return Fail(OperationErrorCode.VerificationFailed);
        var id = Guid.NewGuid();
        var file = new OwnedMediaFile(id, Path.Combine(Path.GetFullPath(directory), $"presentation-ref-{id:N}.bin"), [selected.Format.Id]);
        FileStream? output = null;
        bool owned = false, uncertainClose = false;
        OperationStatus status = OperationStatus.Failed;
        OperationError? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            output = new(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            owned = true;
            await output.WriteAsync(bytes, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            status = OperationStatus.Succeeded;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = OperationStatus.Cancelled; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = Error(OperationErrorCode.OutputFailed); }
        finally
        {
            if (output is not null)
                try { await output.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    uncertainClose = true;
                    if (status == OperationStatus.Succeeded) { status = OperationStatus.Failed; error = Error(OperationErrorCode.OutputFailed); }
                }
        }
        if (status == OperationStatus.Succeeded && token.IsCancellationRequested) status = OperationStatus.Cancelled;
        if (status == OperationStatus.Succeeded) return new(status, new(file, bytes.Length, used + bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), first), null, []);
        var files = new List<OwnedMediaFile>();
        if (uncertainClose) files.Add(file);
        else if (owned)
            try { File.Delete(file.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files.Add(file); }
        return new(status, null, status == OperationStatus.Cancelled ? null : error, files.AsReadOnly());
    }
    private static AuxiliaryHlsCollectionResult FromHttp(SourceHttpReadResult result) => result.Status == OperationStatus.Cancelled ?
        new(OperationStatus.Cancelled, null, null, []) : Fail(result.Failure == SourceHttpFailureKind.SizeLimitExceeded ? OperationErrorCode.UnsupportedFormat :
            result.Failure == SourceHttpFailureKind.TransportFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed);
    private static AuxiliaryHlsCollectionResult Fail(OperationErrorCode code) => new(OperationStatus.Failed, null, Error(code), []);
    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "제한된 presentation 보조 참조를 수집하지 못했습니다.", "ProviderPresentationReference");
}
