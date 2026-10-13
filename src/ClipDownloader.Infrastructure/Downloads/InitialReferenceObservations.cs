using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

// These are demux/decoder observations, never proof of native source origin or coverage.
internal sealed record InitialCodecObservation(string? Name, string? Profile, string? PixelFormat,
    string? SampleFormat, int? SampleRate, int? Channels, int? ExtradataSize);

internal sealed record InitialStreamObservation(int Index, MediaStreamKind Kind,
    SignedMediaTime TimeBase, long? StartPts, FrameRate? DeclaredRate, FrameRate? AverageRate, uint? TrackId,
    InitialCodecObservation Codec);

internal sealed record InitialFrameObservation(int StreamIndex, long? Pts, long? Duration, int? SampleCount,
    string? SampleFormat, int? SampleRate, int? Channels);

internal sealed class InitialReferenceObservations
{
    public MediaTimeBasis TimeBasis => MediaTimeBasis.Demux;
    public IReadOnlyList<InitialStreamObservation> Streams { get; }
    public IReadOnlyList<InitialFrameObservation> Frames { get; }

    internal InitialReferenceObservations(IEnumerable<InitialStreamObservation> streams,
        IEnumerable<InitialFrameObservation> frames)
    {
        Streams = Array.AsReadOnly(streams.ToArray());
        Frames = Array.AsReadOnly(frames.ToArray());
    }
}
