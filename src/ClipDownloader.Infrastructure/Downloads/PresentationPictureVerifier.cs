using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record PresentationPictureEvidence(int PictureCount);

internal sealed class PresentationPictureVerifier
{
    internal OperationResult<PresentationPictureEvidence> Verify(VerifiedHlsNativeReference source,
        H264CarrierObservations carrier, DecodedPictureObservations movie, DecodedPictureObservations transport)
    {
        if (source.Streams.Count != 1 || source.Streams[0].Stream.Kind != MediaStreamKind.Video ||
            source.Reference.ContainerTiming.Tracks.Count != 1 || source.Streams[0].Stream.Codec.PixelFormat != "yuv420p" ||
            carrier.PixelFormat != "yuv420p" || carrier.Width != movie.Width || carrier.Height != movie.Height ||
            movie.Width != transport.Width || movie.Height != transport.Height || movie.Pictures.Count != transport.Pictures.Count)
            return Fail();
        var stream = source.Streams[0].Stream;
        var frames = source.Reference.Observations.Frames.OrderBy(f => f.Pts).ToArray();
        if (frames.Length == 0 || frames.Length != movie.Pictures.Count) return Fail();
        var track = source.Reference.ContainerTiming.Tracks[0];
        var edit = SignedMediaTime.Zero;
        if (track.HasEditList)
        {
            if (track.Edits.Count != 1 || track.Edits[0] is not { MediaTime: >= 0, RateInteger: 1, RateFraction: 0 }) return Fail();
            SignedMediaTime.TryCreate(track.Edits[0].MediaTime, track.MediaTimescale, out edit!, out _);
        }
        for (int i = 0; i < frames.Length; i++)
        {
            var left = movie.Pictures[i]; var right = transport.Pictures[i];
            if (frames[i].Pts is not { } pts || frames[i].Duration is not { } duration || left.Sha256 != right.Sha256 ||
                Time(left.Pts, movie.Clock) != Time(pts, stream.TimeBase) || Time(left.Duration, movie.Clock) != Time(duration, stream.TimeBase) ||
                Time(right.Pts, transport.Clock) - Time(left.Pts, movie.Clock) != edit ||
                Time(right.Duration, transport.Clock) != Time(left.Duration, movie.Clock)) return Fail();
        }
        return OperationResult<PresentationPictureEvidence>.Succeeded(new(frames.Length));
    }
    private static SignedMediaTime Time(long ticks, SignedMediaTime clock)
    {
        SignedMediaTime.TryCreate((BigInteger)ticks * clock.Numerator, clock.Denominator, out var time, out _);
        return time!;
    }
    private static OperationResult<PresentationPictureEvidence> Fail() => OperationResult<PresentationPictureEvidence>.Failed(
        new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "원본과 provider 참조의 decoded picture 값 및 명시 movie 시간 관계를 확인하지 못했습니다.", "DecodedPresentationBridge"));
}
