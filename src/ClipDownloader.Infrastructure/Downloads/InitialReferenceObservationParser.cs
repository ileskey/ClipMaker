using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class InitialReferenceObservationParser
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumFrames = 100_000;
    internal const int MaximumPackets = 100_000;

    public OperationResult<InitialReferenceObservations> Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > MaximumBytes) return Failed();
        try
        {
            // JsonDocument alone accepts replacement characters in some string paths.
            _ = new UTF8Encoding(false, true).GetCharCount(json.Span);
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
            var root = document.RootElement;
            Object(root);
            var streamsJson = Required(root, "streams");
            var frameItems = new List<JsonElement>();
            var packetItems = new List<JsonElement>();
            bool packetsObserved;
            if (root.TryGetProperty("packets_and_frames", out var combined))
            {
                if (root.TryGetProperty("frames", out _) || root.TryGetProperty("packets", out _) ||
                    combined.ValueKind != JsonValueKind.Array || combined.GetArrayLength() > MaximumFrames + MaximumPackets) return Failed();
                foreach (var item in combined.EnumerateArray())
                {
                    Object(item);
                    switch (Required(item, "type").GetString())
                    {
                        case "frame": frameItems.Add(item); break;
                        case "packet": packetItems.Add(item); break;
                        default: return Failed();
                    }
                }
                packetsObserved = true;
            }
            else
            {
                var frameArray = Required(root, "frames");
                if (frameArray.ValueKind != JsonValueKind.Array || frameArray.GetArrayLength() > MaximumFrames) return Failed();
                frameItems.AddRange(frameArray.EnumerateArray());
                packetsObserved = root.TryGetProperty("packets", out var packetArray);
                if (packetsObserved)
                {
                    if (packetArray.ValueKind != JsonValueKind.Array || packetArray.GetArrayLength() > MaximumPackets) return Failed();
                    packetItems.AddRange(packetArray.EnumerateArray());
                }
            }
            if (streamsJson.ValueKind != JsonValueKind.Array || streamsJson.GetArrayLength() is < 1 or > 16 ||
                frameItems.Count > MaximumFrames || packetItems.Count > MaximumPackets)
                return Failed();
            var streams = new List<InitialStreamObservation>();
            foreach (var item in streamsJson.EnumerateArray())
            {
                Object(item);
                int index = checked((int)Integer(Required(item, "index")));
                if (index < 0 || streams.Any(s => s.Index == index)) return Failed();
                var kind = Kind(Required(item, "codec_type").GetString());
                var (n, d) = Rational(Required(item, "time_base").GetString());
                if (n <= 0 || d <= 0 || !SignedMediaTime.TryCreate(n, d, out var timeBase, out _)) return Failed();
                var codec = new InitialCodecObservation(Label(item, "codec_name"), Label(item, "profile"),
                    Label(item, "pix_fmt"), Label(item, "sample_fmt"), PositiveInt(item, "sample_rate"),
                    PositiveInt(item, "channels"), NonnegativeInt(item, "extradata_size"));
                if (kind == MediaStreamKind.Video && (codec.SampleFormat is not null || codec.SampleRate is not null || codec.Channels is not null) ||
                    kind == MediaStreamKind.Audio && codec.PixelFormat is not null) return Failed();
                streams.Add(new(index, kind, timeBase!, OptionalInteger(item, "start_pts"),
                    Rate(item, "r_frame_rate"), Rate(item, "avg_frame_rate"), TrackId(item), codec));
            }
            var frames = new List<InitialFrameObservation>();
            foreach (var item in frameItems)
            {
                Object(item);
                int index = checked((int)Integer(Required(item, "stream_index")));
                var stream = streams.SingleOrDefault(s => s.Index == index);
                if (stream is null || Kind(Required(item, "media_type").GetString()) != stream.Kind) return Failed();
                long? duration = OptionalInteger(item, "duration");
                long? samples = OptionalInteger(item, "nb_samples");
                if (duration < 0 || samples <= 0 || samples > int.MaxValue ||
                    samples is not null && stream.Kind != MediaStreamKind.Audio) return Failed();
                string? sampleFormat = Label(item, "sample_fmt");
                int? sampleRate = PositiveInt(item, "sample_rate");
                int? channels = PositiveInt(item, "channels");
                if (stream.Kind != MediaStreamKind.Audio && (sampleFormat is not null || sampleRate is not null || channels is not null)) return Failed();
                frames.Add(new(index, OptionalInteger(item, "pts"), duration, samples is null ? null : (int)samples,
                    sampleFormat, sampleRate, channels));
            }
            var packets = new List<InitialPacketObservation>();
            foreach (var item in packetItems)
            {
                Object(item);
                int index = checked((int)Integer(Required(item, "stream_index")));
                var stream = streams.SingleOrDefault(stream => stream.Index == index);
                if (stream is null) return Failed();
                long? duration = OptionalInteger(item, "duration");
                if (duration < 0) return Failed();
                string? flags = null;
                if (item.TryGetProperty("flags", out var flagValue))
                {
                    flags = flagValue.GetString();
                    if (flags is null || flags.Length is < 1 or > 16 || !flags.All(c => char.IsAsciiLetter(c) || c == '_')) return Failed();
                }
                InitialPacketSkipObservation? skip = null;
                bool uninterpreted = false;
                if (item.TryGetProperty("side_data_list", out var sideData))
                {
                    if (sideData.ValueKind != JsonValueKind.Array || sideData.GetArrayLength() > 64) return Failed();
                    foreach (var side in sideData.EnumerateArray())
                    {
                        Object(side);
                        string? type = Required(side, "side_data_type").GetString();
                        if (string.IsNullOrEmpty(type)) return Failed();
                        if (type != "Skip Samples") { uninterpreted = true; continue; }
                        if (skip is not null || stream.Kind != MediaStreamKind.Audio) return Failed();
                        skip = new(OptionalUnsigned(side, "skip_samples", uint.MaxValue), OptionalUnsigned(side, "discard_padding", uint.MaxValue),
                            (byte?)OptionalUnsigned(side, "skip_reason", byte.MaxValue), (byte?)OptionalUnsigned(side, "discard_reason", byte.MaxValue));
                    }
                }
                var packetPosition = OptionalInteger(item, "pos");
                if (packetPosition < 0) return Failed();
                packets.Add(new(index, OptionalInteger(item, "pts"), OptionalInteger(item, "dts"), duration, flags, skip, uninterpreted,
                    NonnegativeInt(item, "size"), packetPosition));
            }
            return OperationResult<InitialReferenceObservations>.Succeeded(new(streams, frames, packets, packetsObserved));
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or FormatException or
            OverflowException or InvalidOperationException or ArgumentException)
        {
            return Failed();
        }
    }

    private static void Object(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new FormatException();
    }

    private static JsonElement Required(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value : throw new FormatException();

    private static long Integer(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 20 } text &&
            long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)) return number;
        throw new FormatException();
    }

    private static long? OptionalInteger(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? Integer(value) : null;
    private static uint? OptionalUnsigned(JsonElement item, string name, uint maximum)
    {
        var value = OptionalInteger(item, name);
        if (value < 0 || value > maximum) throw new FormatException();
        return value is null ? null : (uint)value;
    }

    private static int? NonnegativeInt(JsonElement item, string name)
    {
        var value = OptionalInteger(item, name);
        if (value < 0 || value > int.MaxValue) throw new FormatException();
        return value is null ? null : (int)value;
    }
    private static int? PositiveInt(JsonElement item, string name)
    {
        var value = NonnegativeInt(item, name);
        if (value == 0) throw new FormatException();
        return value;
    }
    private static string? Label(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        string? text = value.GetString();
        if (text is "unknown" or "N/A") return null;
        if (text is null || text.Length is < 1 or > 128 ||
            !text.All(character => char.IsAsciiLetterOrDigit(character) || " _().:+-".Contains(character))) throw new FormatException();
        return text;
    }

    private static uint? TrackId(JsonElement item)
    {
        if (!item.TryGetProperty("id", out var value)) return null;
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text &&
            text.StartsWith("0x", StringComparison.Ordinal) && text.Length is > 2 and <= 10)
        {
            if (uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint id) && id > 0)
                return id;
            throw new FormatException();
        }
        long number = Integer(value);
        if (number <= 0 || number > uint.MaxValue) throw new FormatException();
        return (uint)number;
    }

    private static (long Numerator, long Denominator) Rational(string? text)
    {
        if (text is null || text.Length > 41) throw new FormatException();
        var parts = text.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n) ||
            !long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long d)) throw new FormatException();
        return (n, d);
    }

    private static FrameRate? Rate(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        var (n, d) = Rational(value.GetString());
        if (n == 0 && d == 0) return null; // FFprobe's explicit unavailable value.
        if (!FrameRate.TryCreate(n, d, out var rate, out _)) throw new FormatException();
        return rate;
    }

    private static MediaStreamKind Kind(string? kind) => kind switch
    {
        "video" => MediaStreamKind.Video,
        "audio" => MediaStreamKind.Audio,
        _ => throw new FormatException()
    };

    private static OperationResult<InitialReferenceObservations> Failed() => OperationResult<InitialReferenceObservations>.Failed(
        new(OperationErrorCode.InvalidMetadata, OperationErrorCategory.Execution, "초기 참조 관찰 응답이 불완전하거나 지원 범위를 벗어났습니다."));
}
