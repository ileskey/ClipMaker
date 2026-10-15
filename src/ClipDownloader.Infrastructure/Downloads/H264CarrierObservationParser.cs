using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record H264CarrierPicture(long? Pts, H264PicturePayload Payload);
internal sealed class H264CarrierObservations(SignedMediaTime timeBase, IEnumerable<H264CarrierPicture> pictures,
    string? pixelFormat = null, int? width = null, int? height = null)
{
    internal SignedMediaTime TimeBase { get; } = timeBase;
    internal IReadOnlyList<H264CarrierPicture> Pictures { get; } = Array.AsReadOnly(pictures.ToArray());
    internal string? PixelFormat { get; } = pixelFormat;
    internal int? Width { get; } = width;
    internal int? Height { get; } = height;
}

internal sealed class H264CarrierObservationParser
{
    internal OperationResult<H264CarrierObservations> Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (bytes.Length is < 1 or > 16 * 1024 * 1024) throw new InvalidDataException();
            _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            var root = document.RootElement; Object(root);
            var streams = root.GetProperty("streams");
            if (streams.ValueKind != JsonValueKind.Array || streams.GetArrayLength() != 1) throw new InvalidDataException();
            var stream = streams[0]; Object(stream);
            int index = checked((int)Integer(stream.GetProperty("index")));
            if (index < 0 || stream.GetProperty("codec_type").GetString() != "video" || stream.GetProperty("codec_name").GetString() != "h264" ||
                stream.GetProperty("time_base").GetString() != "1/90000") throw new InvalidDataException();
            SignedMediaTime.TryCreate(1, 90000, out var clock, out _);
            var packets = root.GetProperty("packets");
            if (packets.ValueKind != JsonValueKind.Array || packets.GetArrayLength() is < 1 or > 4096) throw new InvalidDataException();
            var pictures = new List<H264CarrierPicture>();
            int total = 0;
            foreach (var packet in packets.EnumerateArray())
            {
                Object(packet);
                if (Integer(packet.GetProperty("stream_index")) != index) throw new InvalidDataException();
                long? pts = packet.TryGetProperty("pts", out var stamp) ? Integer(stamp) : null;
                string flags = packet.GetProperty("flags").GetString() ?? throw new InvalidDataException();
                if (flags.Length is < 1 or > 16 || flags.Any(c => !(char.IsAsciiLetter(c) || c == '_')) || flags.Contains('C') || flags.Contains('D'))
                    throw new InvalidDataException();
                int size = checked((int)Integer(packet.GetProperty("size")));
                if (size <= 0 || size > 4 * 1024 * 1024 - total) throw new InvalidDataException();
                byte[] payload = Hex(packet.GetProperty("data").GetString() ?? throw new InvalidDataException(), size);
                total += payload.Length;
                var canonical = new H264PicturePayloadReader().Read(payload, true);
                if (canonical.Status != OperationStatus.Succeeded) throw new InvalidDataException();
                pictures.Add(new(pts, canonical.Data!));
            }
            string? pixel = stream.TryGetProperty("pix_fmt", out var pix) ? pix.GetString() : null;
            if (pixel is not null && (pixel.Length is < 1 or > 32 || pixel.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) throw new InvalidDataException();
            int? width = stream.TryGetProperty("width", out var w) ? checked((int)Integer(w)) : null;
            int? height = stream.TryGetProperty("height", out var h) ? checked((int)Integer(h)) : null;
            if (width is < 1 or > 8192 || height is < 1 or > 8192) throw new InvalidDataException();
            return OperationResult<H264CarrierObservations>.Succeeded(new(clock!, pictures, pixel, width, height));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or JsonException)
        { return OperationResult<H264CarrierObservations>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "H.264 carrier packet 관찰의 완전성과 payload를 확인하지 못했습니다.", "CarrierPacketObservation")); }
    }

    private static long Integer(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)) return number;
        throw new InvalidDataException();
    }
    private static void Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name)) throw new InvalidDataException();
    }
    private static byte[] Hex(string dump, int size)
    {
        if (dump.Length > size * 6 + 4096) throw new InvalidDataException();
        var bytes = new List<byte>(size);
        foreach (string line in dump.Split('\n'))
        {
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon != 8 || !int.TryParse(line[..colon], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int offset) || offset != bytes.Count)
                throw new InvalidDataException();
            string hexPart = line[(colon + 1)..].TrimStart();
            int separator = hexPart.IndexOf("  ", StringComparison.Ordinal);
            if (separator < 0) throw new InvalidDataException();
            string hex = hexPart[..separator].Replace(" ", "", StringComparison.Ordinal);
            if (hex.Length is < 2 or > 32 || hex.Length % 2 != 0 || hex.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException();
            var row = Convert.FromHexString(hex);
            if (row.Length > size - bytes.Count) throw new InvalidDataException();
            bytes.AddRange(row);
        }
        if (bytes.Count != size) throw new InvalidDataException();
        return bytes.ToArray();
    }
}
