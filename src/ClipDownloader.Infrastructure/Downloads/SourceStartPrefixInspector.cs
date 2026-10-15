using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;

namespace ClipDownloader.Infrastructure.Downloads;

// This establishes captured source-start bytes, not decoder state or PCM values.
internal sealed class SourceStartPrefixInspector
{
    internal async Task<OperationResult<SourceStartPrefixEvidence>> InspectAsync(AcquiredMedia acquisition,
        OwnedMediaFile target, CancellationToken token)
    {
        var candidates = acquisition.Request.Inspection.Files.Where(f => f.SourceFormatIds.SequenceEqual(target.SourceFormatIds)).ToArray();
        if (candidates.Length != 1 || candidates[0].Content is null || target.Content is null || candidates[0].Content!.Bytes > target.Content.Bytes)
            return Fail("SourceStartPrefixUnavailable");
        var first = candidates[0];
        try
        {
            await using var original = new FileStream(first.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await using var input = new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (!await Matches(original, first.Content!, token).ConfigureAwait(false) || !await Matches(input, target.Content, token).ConfigureAwait(false))
                return Fail("SourceStartByteIdentity");
            input.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long remaining = first.Content!.Bytes;
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await input.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count); remaining -= count;
            }
            string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (digest != first.Content.Sha256 || !await Matches(original, first.Content, token).ConfigureAwait(false) ||
                !await Matches(input, target.Content, token).ConfigureAwait(false)) return Fail("SourceStartPrefixChanged");
            token.ThrowIfCancellationRequested();
            return OperationResult<SourceStartPrefixEvidence>.Succeeded(new(first.Id, first.Content, target.Id, target.Content, digest));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<SourceStartPrefixEvidence>.Cancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("SourceStartPrefixRead"); }
    }

    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity identity, CancellationToken token)
    {
        if (input.Length != identity.Bytes) return false;
        input.Position = 0;
        var hash = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return input.Length == identity.Bytes && Convert.ToHexString(hash).Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static OperationResult<SourceStartPrefixEvidence> Fail(string field) => OperationResult<SourceStartPrefixEvidence>.Failed(
        new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "선택 원본 시작의 captured bytes와 획득 prefix의 대응을 확인하지 못했습니다.", field));
}
