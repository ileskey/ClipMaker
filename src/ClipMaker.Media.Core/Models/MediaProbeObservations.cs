using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

// Raw demux/decoder observations. Average rate and packet flags are not cadence,
// native time, decoder preparation, or source coverage proofs.
public sealed record MediaCodecObservation(string? Name, string? Profile, string? PixelFormat,
    string? SampleFormat, int? SampleRate, int? Channels, int? ExtradataSize);
public sealed record MediaStreamObservation(int Index, MediaStreamKind Kind, SignedMediaTime TimeBase,
    long? StartPts, FrameRate? DeclaredRate, FrameRate? AverageRate, uint? TrackId, MediaCodecObservation Codec);
public sealed record MediaFrameObservation(int StreamIndex, long? Pts, long? Duration, int? SampleCount,
    string? SampleFormat, int? SampleRate, int? Channels);
public sealed record MediaPacketSkipObservation(uint? SkipSamples, uint? DiscardPadding, byte? SkipReason, byte? DiscardReason);
// Bounded encoded-byte evidence; a recovery point is not a closed IDR access point.
public sealed record MediaH264RecoveryPointObservation(long Position, int Size, string Sha256,
    int RecoveryFrameCount, bool ExactMatch, bool BrokenLink, int ChangingSliceGroupIdc, bool AllSlicesIntra);
public sealed record MediaPacketObservation(int StreamIndex, long? Pts, long? Dts, long? Duration,
    string? Flags, MediaPacketSkipObservation? Skip, bool HasUninterpretedSideData, int? Size, long? Position,
    MediaH264RecoveryPointObservation? RecoveryPoint = null);

public sealed class MediaProbeObservations
{
    public MediaTimeBasis TimeBasis => MediaTimeBasis.Demux;
    public IReadOnlyList<MediaStreamObservation> Streams { get; }
    public IReadOnlyList<MediaFrameObservation> Frames { get; }
    public IReadOnlyList<MediaPacketObservation> Packets { get; }
    public bool FramesObserved { get; }
    public bool PacketsObserved { get; }

    public MediaProbeObservations(IEnumerable<MediaStreamObservation> streams, IEnumerable<MediaFrameObservation> frames,
        IEnumerable<MediaPacketObservation> packets, bool framesObserved, bool packetsObserved)
    {
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(packets);
        Streams = Array.AsReadOnly(streams.ToArray());
        Frames = Array.AsReadOnly(frames.ToArray());
        Packets = Array.AsReadOnly(packets.ToArray());
        if (Streams.Count == 0 || Streams.Any(s => s is null || s.Index < 0 || s.TimeBase is null || s.TimeBase.Numerator <= 0 || s.Codec is null || !Enum.IsDefined(s.Kind)) ||
            Streams.Select(s => s.Index).Distinct().Count() != Streams.Count)
            throw new ArgumentException("Unique local streams and positive clocks are required.", nameof(streams));
        if (Frames.Any(f => f is null || !Streams.Any(s => s.Index == f.StreamIndex)) ||
            Packets.Any(p => p is null || !Streams.Any(s => s.Index == p.StreamIndex)) ||
            !framesObserved && Frames.Count != 0 || !packetsObserved && Packets.Count != 0)
            throw new ArgumentException("Observations must belong to the observed streams.");
        FramesObserved = framesObserved;
        PacketsObserved = packetsObserved;
    }
}
