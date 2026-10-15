using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Probing;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record Mp4FileTimingObservation(Mp4TimingObservation Initialization,
    IReadOnlyList<Mp4FragmentDecodeAnchor> FragmentAnchors, OperationResult<Mp4NativeSampleEvidence> NativeSamples);
internal sealed record Mp4MoofBody(long Offset, ReadOnlyMemory<byte> Body);
internal sealed record Mp4PayloadRange(long Start, long End);

// Acquisition error boundary over the shared, site-independent container reader.
internal sealed class Mp4TimingReader
{
    internal const int MaximumMoovBytes = 2 * 1024 * 1024;
    internal const int MaximumMoofBytes = 2 * 1024 * 1024;
    internal const int MaximumTotalMoofBytes = 8 * 1024 * 1024;

    internal async Task<OperationResult<Mp4FileTimingObservation>> ReadAsync(Stream input, CancellationToken token)
    {
        var result = await new LocalMp4TimingReader().ReadAsync(input, token).ConfigureAwait(false);
        if (result.Status == MediaOperationStatus.Cancelled) return OperationResult<Mp4FileTimingObservation>.Cancelled();
        if (result.Status == MediaOperationStatus.Failed) return OperationResult<Mp4FileTimingObservation>.Failed(Error(result.Failure!));
        var data = result.Data!;
        return OperationResult<Mp4FileTimingObservation>.Succeeded(new(data.Initialization, data.FragmentAnchors, Convert(data.NativeSamples)));
    }

    internal OperationResult<Mp4TimingObservation> ParseMoov(ReadOnlyMemory<byte> bytes) =>
        Convert(new LocalMp4TimingReader().ParseMoov(bytes));

    internal static OperationResult<T> Convert<T>(MediaOperationResult<T> result) where T : class => result.Status switch
    {
        MediaOperationStatus.Succeeded => OperationResult<T>.Succeeded(result.Data!),
        MediaOperationStatus.Cancelled => OperationResult<T>.Cancelled(),
        _ => OperationResult<T>.Failed(Error(result.Failure!))
    };

    private static OperationError Error(MediaFailure failure) => new(
        failure.Kind == MediaFailureKind.UnsupportedInput ? OperationErrorCode.UnsupportedFormat : OperationErrorCode.InvalidMetadata,
        OperationErrorCategory.Execution, failure.Message, failure.Field);
}
