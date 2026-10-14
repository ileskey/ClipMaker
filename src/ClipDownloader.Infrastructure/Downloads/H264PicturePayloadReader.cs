using System.Buffers.Binary;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record H264PicturePayload(string Sha256, bool ContainsIdr, int SliceCount, bool AllSlicesIdr = false);

// Canonical VCL payload correspondence. Decoder/configuration equality is a separate gate.
internal sealed class H264PicturePayloadReader
{
    internal OperationResult<H264PicturePayload> Read(ReadOnlyMemory<byte> packet, bool annexB)
    {
        try
        {
            if (packet.Length is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException();
            var spans = new List<(int Start, int Length)>();
            var bytes = packet.Span;
            if (!annexB)
            {
                for (int at = 0; at < bytes.Length;)
                {
                    if (bytes.Length - at < 4) throw new InvalidDataException();
                    int size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[at..])); at += 4;
                    if (size <= 0 || size > bytes.Length - at) throw new InvalidDataException();
                    spans.Add((at, size)); at += size;
                    if (spans.Count > 4096) throw new InvalidDataException();
                }
            }
            else
            {
                var starts = new List<(int At, int Prefix)>();
                for (int at = 0; at + 3 <= bytes.Length; at++)
                {
                    int prefix = bytes[at] == 0 && bytes[at + 1] == 0 ?
                        at + 4 <= bytes.Length && bytes[at + 2] == 0 && bytes[at + 3] == 1 ? 4 : bytes[at + 2] == 1 ? 3 : 0 : 0;
                    if (prefix == 0) continue;
                    starts.Add((at, prefix)); at += prefix - 1;
                    if (starts.Count > 4096) throw new InvalidDataException();
                }
                if (starts.Count == 0 || bytes[..starts[0].At].IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException();
                for (int i = 0; i < starts.Count; i++)
                {
                    int begin = starts[i].At + starts[i].Prefix;
                    spans.Add((begin, (i + 1 < starts.Count ? starts[i + 1].At : bytes.Length) - begin));
                }
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> sizeBytes = stackalloc byte[4];
            int slices = 0; bool idr = false, allIdr = true;
            foreach (var span in spans)
            {
                if (span.Length == 0) throw new InvalidDataException();
                var nal = bytes.Slice(span.Start, span.Length);
                int kind = nal[0] & 31;
                if ((nal[0] & 128) != 0 || kind is 0 or > 12) throw new InvalidDataException();
                if (kind is < 1 or > 5) continue;
                // Annex B trailing zeros and CABAC zero words do not identify a different picture.
                // This normalization never establishes decoded equality on its own.
                int length = nal.Length;
                while (length > 1 && nal[length - 1] == 0) length--;
                if (length <= 1) throw new InvalidDataException();
                BinaryPrimitives.WriteInt32BigEndian(sizeBytes, length);
                hash.AppendData(sizeBytes); hash.AppendData(nal[..length]);
                slices++; idr |= kind == 5; allIdr &= kind == 5;
            }
            if (slices == 0) throw new InvalidDataException();
            return OperationResult<H264PicturePayload>.Succeeded(new(Convert.ToHexString(hash.GetHashAndReset()), idr, slices, allIdr));
        }
        catch (Exception e) when (e is InvalidDataException or OverflowException)
        { return OperationResult<H264PicturePayload>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "H.264 picture payload의 제한된 framing 대응을 확인하지 못했습니다.", "PicturePayload")); }
    }
}
