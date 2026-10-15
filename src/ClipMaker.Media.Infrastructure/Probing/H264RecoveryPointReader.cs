using System.Buffers.Binary;
using System.Security.Cryptography;
using ClipMaker.Media.Core.Models;

namespace ClipMaker.Media.Infrastructure.Probing;

// Limited AVC framing: exact zero-count recovery SEI and non-IDR intra slices only.
public sealed class H264RecoveryPointReader
{
    internal MediaH264RecoveryPointObservation? Read(ReadOnlySpan<byte> bytes, long position)
    {
        if (position < 0 || bytes.Length is < 1 or > 4 * 1024 * 1024) return null;
        int at = 0, recovery = 0, slices = 0, nals = 0;
        Span<byte> header = stackalloc byte[32];
        while (at < bytes.Length)
        {
            if (bytes.Length - at < 4 || ++nals > 4096) return null;
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]); at += 4;
            if (length == 0 || length > bytes.Length - at) return null;
            var nal = bytes.Slice(at, (int)length); at += (int)length;
            if ((nal[0] & 128) != 0) return null;
            if ((nal[0] & 31) == 6)
            {
                // recovery_frame_cnt=0, exact_match=1, broken_link=0, group_idc=0.
                if (!nal.SequenceEqual(new byte[] { 6, 6, 1, 0xc4, 0x80 }) || ++recovery != 1) return null;
            }
            else if ((nal[0] & 31) == 1)
            {
                int count = 0, zeros = 0;
                for (int i = 1; i < nal.Length && count < header.Length; i++)
                {
                    byte b = nal[i];
                    if (zeros >= 2 && b == 3)
                    {
                        if (i + 1 >= nal.Length || nal[i + 1] > 3) return null;
                        zeros = 0; continue;
                    }
                    header[count++] = b; zeros = b == 0 ? zeros + 1 : 0;
                }
                int bit = 0;
                if (!Ue(header[..count], ref bit, out _) || !Ue(header[..count], ref bit, out uint kind) ||
                    kind is not (2 or 7) || !Ue(header[..count], ref bit, out uint pps) || pps > 255) return null;
                slices++;
            }
            else return null;
        }
        return recovery == 1 && slices > 0 ? new(position, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
            0, true, false, 0, true) : null;
    }

    private static bool Ue(ReadOnlySpan<byte> bytes, ref int bit, out uint value)
    {
        value = 0; int zeros = 0;
        while (true)
        {
            if (bit >= bytes.Length * 8) return false;
            int b = (bytes[bit / 8] >> (7 - bit % 8)) & 1; bit++;
            if (b == 1) break;
            if (++zeros > 31) return false;
        }
        if (bit + zeros > bytes.Length * 8) return false;
        uint suffix = 0;
        for (int i = 0; i < zeros; i++) suffix = (suffix << 1) | (uint)((bytes[bit / 8] >> (7 - bit++ % 8)) & 1);
        value = ((1u << zeros) - 1) + suffix; return true;
    }

    public async Task<MediaProbeObservations> EnrichAsync(Stream file, MediaProbeObservations observations,
        Mp4FileTimingEvidence container, CancellationToken token)
    {
        if (container.NativeSamples?.Data is not { } native) return observations;
        var eligible = native.Samples.Where(s => (s.Flags & 0x10000) != 0)
            .Select(s => (s.TrackId, s.ByteOffset, s.Size)).ToHashSet();
        var packets = new List<MediaPacketObservation>();
        foreach (var packet in observations.Packets)
        {
            token.ThrowIfCancellationRequested();
            var stream = observations.Streams.Single(s => s.Index == packet.StreamIndex);
            var current = packet;
            if (stream.Kind == MediaStreamKind.Video && stream.Codec.Name == "h264" && stream.TrackId is { } track &&
                packet.Flags?.Contains('K') == true && packet.Position is >= 0 and { } position &&
                packet.Size is > 0 and <= 4 * 1024 * 1024 and { } size && eligible.Contains((track, position, (uint)size)) &&
                position <= file.Length - size)
            {
                byte[] bytes = new byte[size]; file.Position = position;
                await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                current = packet with { RecoveryPoint = Read(bytes, position) };
            }
            packets.Add(current);
        }
        return new(observations.Streams, observations.Frames, packets, observations.FramesObserved, observations.PacketsObserved);
    }
}
