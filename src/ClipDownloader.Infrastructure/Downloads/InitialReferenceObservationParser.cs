using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Probing;

namespace ClipDownloader.Infrastructure.Downloads;

// Acquisition retains its existing result boundary while sharing the strict raw parser.
internal sealed class InitialReferenceObservationParser
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumFrames = 100_000;
    internal const int MaximumPackets = 100_000;

    public OperationResult<InitialReferenceObservations> Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaximumBytes) return Failed();
        var result = new FfprobeObservationParser().Parse(json);
        if (result.Status != MediaOperationStatus.Succeeded) return Failed();
        return OperationResult<InitialReferenceObservations>.Succeeded(FromShared(result.Data!));
    }

    internal InitialReferenceObservations FromShared(MediaProbeObservations raw) => new(
            raw.Streams.Select(s => new InitialStreamObservation(s.Index, s.Kind, s.TimeBase, s.StartPts, s.DeclaredRate,
                s.AverageRate, s.TrackId, new(s.Codec.Name, s.Codec.Profile, s.Codec.PixelFormat, s.Codec.SampleFormat,
                    s.Codec.SampleRate, s.Codec.Channels, s.Codec.ExtradataSize))),
            raw.Frames.Select(f => new InitialFrameObservation(f.StreamIndex, f.Pts, f.Duration, f.SampleCount,
                f.SampleFormat, f.SampleRate, f.Channels)),
            raw.Packets.Select(p => new InitialPacketObservation(p.StreamIndex, p.Pts, p.Dts, p.Duration, p.Flags,
                p.Skip is null ? null : new(p.Skip.SkipSamples, p.Skip.DiscardPadding, p.Skip.SkipReason, p.Skip.DiscardReason),
                p.HasUninterpretedSideData, p.Size, p.Position, p.RecoveryPoint)), raw.PacketsObserved);

    private static OperationResult<InitialReferenceObservations> Failed() => OperationResult<InitialReferenceObservations>.Failed(
        new(OperationErrorCode.InvalidMetadata, OperationErrorCategory.Execution, "초기 참조 관찰 응답이 불완전하거나 지원 범위를 벗어났습니다."));
}
