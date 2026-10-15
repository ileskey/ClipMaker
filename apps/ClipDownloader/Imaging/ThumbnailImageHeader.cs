using System.Buffers.Binary;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Imaging;

internal static class ThumbnailImageHeader
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    internal static bool TryRead(ReadOnlySpan<byte> bytes, ThumbnailImageKind kind, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length is 0 or > ThumbnailContentResult.MaximumBytes) return false;
        bool found = false;
        if (kind == ThumbnailImageKind.Png)
        {
            if (!bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
            int offset = 8; bool data = false, end = false;
            while (offset <= bytes.Length - 12)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
                if (length > (uint)(bytes.Length - offset - 12)) return false;
                uint crc = uint.MaxValue;
                foreach (byte value in bytes.Slice(offset + 4, (int)length + 4)) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
                if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8 + (int)length)..])) return false;
                var type = bytes.Slice(offset + 4, 4);
                if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) return false;
                if (type.SequenceEqual("IHDR"u8))
                {
                    if (found || offset != 8 || length != 13) return false;
                    uint w = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8)..]);
                    uint h = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 12)..]);
                    if (w > 4096 || h > 4096) return false;
                    width = (int)w; height = (int)h; found = true;
                }
                if (!found) return false;
                if (type.SequenceEqual("IDAT"u8)) data = true;
                offset += (int)length + 12;
                if (type.SequenceEqual("IEND"u8)) { end = length == 0 && offset == bytes.Length; break; }
            }
            if (!data || !end) return false;
        }
        else if (kind == ThumbnailImageKind.Jpeg)
        {
            if (!bytes.StartsWith(new byte[] { 255, 216, 255 })) return false;
            int offset = 2;
            while (offset < bytes.Length)
            {
                if (bytes[offset++] != 255) return false;
                while (offset < bytes.Length && bytes[offset] == 255) offset++;
                if (offset >= bytes.Length) return false;
                byte marker = bytes[offset++];
                if (marker == 0xDA) break; // Entropy data is validated by WIC, never scanned as segment headers.
                if (marker is 0xD9 or 0xD8 or 0x00 || marker is >= 0xD0 and <= 0xD7) return false;
                if (offset > bytes.Length - 2) return false;
                int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
                if (length < 2 || length > bytes.Length - offset) return false;
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                {
                    if (found || marker is not (0xC0 or 0xC1 or 0xC2) || length < 8) return false;
                    height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]);
                    width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]); found = true;
                }
                offset += length;
            }
            if (bytes.Length < 2 || bytes[^2] != 255 || bytes[^1] != 217) return false;
        }
        else return false;
        return found && width is > 0 and <= 4096 && height is > 0 and <= 4096 && (long)width * height <= 8_000_000;
    }
}
