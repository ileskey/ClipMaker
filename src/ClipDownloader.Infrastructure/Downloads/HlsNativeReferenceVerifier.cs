using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record VerifiedNativeStream(InitialStreamObservation Stream, Mp4TrackConfiguration Configuration,
    PresentationRange ObservedRange);
internal sealed class VerifiedHlsNativeReference(ProbedInitialReference reference, IEnumerable<VerifiedNativeStream> streams)
{
    internal ProbedInitialReference Reference { get; } = reference;
    internal IReadOnlyList<VerifiedNativeStream> Streams { get; } = Array.AsReadOnly(streams.ToArray());
    // Only valid when this is the first whole source reference; the caller establishes that provenance.
    internal SignedMediaTime EarliestPresentation => Streams.Select(s => s.ObservedRange.Start).Min()!;
}

internal sealed class HlsNativeReferenceVerifier
{
    internal OperationResult<VerifiedHlsNativeReference> Verify(ProbedInitialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.NativeSamples is null) return Fail(OperationErrorCode.VerificationFailed, "NativeSamplesMissing");
        if (reference.NativeSamples.Status != OperationStatus.Succeeded)
            return OperationResult<VerifiedHlsNativeReference>.Failed(reference.NativeSamples.Error!);
        bool indexed = reference.Reference is IndexedCollectedReference;
        if (!reference.Observations.PacketsObserved || !indexed && reference.ContainerTiming.Tracks.Any(track => track.HasEditList))
            return Fail(OperationErrorCode.UnsupportedFormat, "NativeEditsUnsupported");
        if ((!indexed && reference.Observations.Streams.Count(s => s.Kind == MediaStreamKind.Video) != 1) ||
            (indexed && reference.Observations.Streams.Count != 1))
            return Fail(OperationErrorCode.UnsupportedFormat, "NativeTrackLayout");
        var container = new Mp4FileTimingEvidence(reference.ContainerTiming, reference.FragmentAnchors,
            MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(reference.NativeSamples.Data!));
        var result = new ClipMaker.Media.Infrastructure.Probing.Mp4NativeObservationVerifier().Verify(reference.Observations.ToShared(), container);
        if (result.Status != MediaOperationStatus.Succeeded)
            return Fail(result.Failure!.Kind == MediaFailureKind.UnsupportedInput ? OperationErrorCode.UnsupportedFormat :
                OperationErrorCode.VerificationFailed, result.Failure.Field ?? "NativeObservation");
        return OperationResult<VerifiedHlsNativeReference>.Succeeded(new(reference, result.Data!.Streams.Select(s =>
            new VerifiedNativeStream(reference.Observations.Streams.Single(raw => raw.Index == s.Stream.Index), s.Configuration, s.ObservedRange))));
    }
    private static OperationResult<VerifiedHlsNativeReference> Fail(OperationErrorCode code, string field) =>
        OperationResult<VerifiedHlsNativeReference>.Failed(new(code, OperationErrorCategory.Execution,
            "HLS 원본 참조의 native sample 관계를 지원 범위에서 확인하지 못했습니다.", field));
}
