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
internal sealed record InitialPacketSkipObservation(uint? SkipSamples, uint? DiscardPadding, byte? SkipReason, byte? DiscardReason);
internal sealed record InitialPacketObservation(int StreamIndex, long? Pts, long? Dts, long? Duration,
    string? Flags, InitialPacketSkipObservation? Skip, bool HasUninterpretedSideData, int? Size, long? Position,
    MediaH264RecoveryPointObservation? RecoveryPoint = null);

internal sealed class InitialReferenceObservations
{
    public MediaTimeBasis TimeBasis => MediaTimeBasis.Demux;
    public IReadOnlyList<InitialStreamObservation> Streams { get; }
    public IReadOnlyList<InitialFrameObservation> Frames { get; }
    public IReadOnlyList<InitialPacketObservation> Packets { get; }
    public bool PacketsObserved { get; }

    internal InitialReferenceObservations(IEnumerable<InitialStreamObservation> streams,
        IEnumerable<InitialFrameObservation> frames, IEnumerable<InitialPacketObservation> packets, bool packetsObserved)
    {
        Streams = Array.AsReadOnly(streams.ToArray());
        Frames = Array.AsReadOnly(frames.ToArray());
        Packets = Array.AsReadOnly(packets.ToArray());
        PacketsObserved = packetsObserved;
    }

    internal MediaProbeObservations ToShared() => new(
        Streams.Select(s => new MediaStreamObservation(s.Index, s.Kind, s.TimeBase, s.StartPts, s.DeclaredRate,
            s.AverageRate, s.TrackId, new(s.Codec.Name, s.Codec.Profile, s.Codec.PixelFormat, s.Codec.SampleFormat,
                s.Codec.SampleRate, s.Codec.Channels, s.Codec.ExtradataSize))),
        Frames.Select(f => new MediaFrameObservation(f.StreamIndex, f.Pts, f.Duration, f.SampleCount, f.SampleFormat, f.SampleRate, f.Channels)),
        Packets.Select(p => new MediaPacketObservation(p.StreamIndex, p.Pts, p.Dts, p.Duration, p.Flags,
            p.Skip is null ? null : new(p.Skip.SkipSamples, p.Skip.DiscardPadding, p.Skip.SkipReason, p.Skip.DiscardReason),
            p.HasUninterpretedSideData, p.Size, p.Position, p.RecoveryPoint)), true, PacketsObserved);
}
