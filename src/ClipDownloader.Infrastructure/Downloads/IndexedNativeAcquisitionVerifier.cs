using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class IndexedNativeAcquisitionVerifier
{
    internal OperationResult<VerifiedHlsNativeReference> Verify(VerifiedHlsNativeReference initial,
        ProbedInitialReference observed, PresentationRange required)
    {
        var verified = new HlsNativeReferenceVerifier().Verify(observed);
        if (verified.Status != OperationStatus.Succeeded) return verified;
        var current = verified.Data!;
        if (initial.Streams.Count != 1 || current.Streams.Count != 1 || observed.Reference is not IndexedCollectedReference file)
            return Fail();
        var native = current.Streams[0]; var original = initial.Streams[0];
        if (native.Stream.Kind != original.Stream.Kind || native.Configuration != original.Configuration ||
            native.Stream.TimeBase != original.Stream.TimeBase || observed.ContainerTiming.MovieTimescale != initial.Reference.ContainerTiming.MovieTimescale ||
            observed.ContainerTiming.Tracks.Count != 1 || initial.Reference.ContainerTiming.Tracks.Count != 1 ||
            !SameTrack(observed.ContainerTiming.Tracks[0], initial.Reference.ContainerTiming.Tracks[0]) ||
            native.ObservedRange != file.ActualIndexRange || native.ObservedRange.Start.CompareTo(required.Start) > 0 ||
            native.ObservedRange.End.CompareTo(required.End) < 0) return Fail();
        return verified;
    }
    private static bool SameTrack(Mp4TrackTiming left, Mp4TrackTiming right) => left.TrackId == right.TrackId &&
        left.MediaTimescale == right.MediaTimescale && left.HasEditList == right.HasEditList && left.Edits.SequenceEqual(right.Edits);
    private static OperationResult<VerifiedHlsNativeReference> Fail() => OperationResult<VerifiedHlsNativeReference>.Failed(
        new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "Indexed 원본의 변하지 않은 movie/native 시간과 요청 support를 확인하지 못했습니다.", "IndexedNativeClockChanged"));
}
