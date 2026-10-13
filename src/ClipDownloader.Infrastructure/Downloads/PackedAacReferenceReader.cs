using System.Buffers.Binary;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record PackedAacFrame(string PayloadSha256, int SampleRate, int Channels, int Samples);
internal sealed class PackedAacReference(ulong transportTicks, IEnumerable<PackedAacFrame> frames)
{
    internal ulong TransportTicks { get; } = transportTicks;
    internal IReadOnlyList<PackedAacFrame> Frames { get; } = Array.AsReadOnly(frames.ToArray());
}

// One complete packed-audio segment. The ID3 carrier clock is distinct from AAC-demux generated PTS.
internal sealed class PackedAacReferenceReader
{
    internal OperationResult<PackedAacReference> Read(ReadOnlyMemory<byte> memory)
    {
        try
        {
            var bytes = memory.Span;
            if (bytes.Length is < 10 or > 4 * 1024 * 1024 || !bytes[..3].SequenceEqual("ID3"u8)) throw new InvalidDataException();
            byte version = bytes[3];
            if (version is not (3 or 4) || bytes[4] != 0 || bytes[5] != 0) return Unsupported();
            int tagSize = SyncSafe(bytes.Slice(6, 4));
            if (tagSize > 65536 || tagSize > bytes.Length - 10) throw new InvalidDataException();
            int end = 10 + tagSize, at = 10, count = 0;
            ulong? stamp = null;
            while (at < end)
            {
                if (bytes[at] == 0)
                { if (bytes[at..end].IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException(); break; }
                if (++count > 64 || end - at < 10) throw new InvalidDataException();
                var id = bytes.Slice(at, 4);
                foreach (byte letter in id) if (!(letter is >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9')) throw new InvalidDataException();
                int size = version == 4 ? SyncSafe(bytes.Slice(at + 4, 4)) : checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[(at + 4)..]));
                if (size <= 0 || size > end - at - 10) throw new InvalidDataException();
                if (bytes[at + 8] != 0 || bytes[at + 9] != 0) return Unsupported();
                var body = bytes.Slice(at + 10, size);
                if (id.SequenceEqual("PRIV"u8))
                {
                    int zero = body.IndexOf((byte)0);
                    if (zero < 0) throw new InvalidDataException();
                    if (body[..zero].SequenceEqual("com.apple.streaming.transportStreamTimestamp"u8))
                    {
                        if (stamp is not null || body.Length != zero + 9) throw new InvalidDataException();
                        ulong value = BinaryPrimitives.ReadUInt64BigEndian(body[(zero + 1)..]);
                        if (value > 0x1ffffffff) throw new InvalidDataException();
                        stamp = value;
                    }
                }
                at += 10 + size;
            }
            if (stamp is null) throw new InvalidDataException();
            at = end;
            var frames = new List<PackedAacFrame>();
            int[] rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350];
            while (at < bytes.Length)
            {
                if (frames.Count == 10000 || bytes.Length - at < 7) throw new InvalidDataException();
                var h = bytes[at..];
                if (h[0] != 255 || (h[1] & 0xf6) != 0xf0) throw new InvalidDataException();
                if ((h[1] & 8) != 0 || (h[2] >> 6) != 1 || (h[6] & 3) != 0) return Unsupported();
                int rate = (h[2] >> 2) & 15, channels = ((h[2] & 1) << 2) | (h[3] >> 6);
                if (rate >= rates.Length || channels is < 1 or > 2) return Unsupported();
                int header = (h[1] & 1) == 1 ? 7 : 9;
                int length = ((h[3] & 3) << 11) | (h[4] << 3) | (h[5] >> 5);
                if (length <= header || length > h.Length) throw new InvalidDataException();
                frames.Add(new(Convert.ToHexString(SHA256.HashData(h.Slice(header, length - header))), rates[rate], channels, 1024));
                at += length;
            }
            if (frames.Count == 0 || frames.Any(f => f.SampleRate != frames[0].SampleRate || f.Channels != frames[0].Channels))
                throw new InvalidDataException();
            return OperationResult<PackedAacReference>.Succeeded(new(stamp.Value, frames));
        }
        catch (Exception e) when (e is InvalidDataException or OverflowException)
        { return Failure(OperationErrorCode.InvalidMetadata); }
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4 || bytes.IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0) throw new InvalidDataException();
        return (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];
    }
    private static OperationResult<PackedAacReference> Unsupported() => Failure(OperationErrorCode.UnsupportedFormat);
    private static OperationResult<PackedAacReference> Failure(OperationErrorCode code) => OperationResult<PackedAacReference>.Failed(
        new(code, OperationErrorCategory.Execution, "Packed AAC 참조의 명시 carrier timestamp와 frame payload를 확인하지 못했습니다.", "PackedAudioReference"));
}
