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
            var framesJson = Required(root, "frames");
            if (streamsJson.ValueKind != JsonValueKind.Array || framesJson.ValueKind != JsonValueKind.Array ||
                streamsJson.GetArrayLength() is < 1 or > 16 || framesJson.GetArrayLength() > MaximumFrames)
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
                streams.Add(new(index, kind, timeBase!, OptionalInteger(item, "start_pts"),
                    Rate(item, "r_frame_rate"), Rate(item, "avg_frame_rate")));
            }
            var frames = new List<InitialFrameObservation>();
            foreach (var item in framesJson.EnumerateArray())
            {
                Object(item);
                int index = checked((int)Integer(Required(item, "stream_index")));
                var stream = streams.SingleOrDefault(s => s.Index == index);
                if (stream is null || Kind(Required(item, "media_type").GetString()) != stream.Kind) return Failed();
                long? duration = OptionalInteger(item, "duration");
                long? samples = OptionalInteger(item, "nb_samples");
                if (duration < 0 || samples <= 0 || samples > int.MaxValue ||
                    samples is not null && stream.Kind != MediaStreamKind.Audio) return Failed();
                frames.Add(new(index, OptionalInteger(item, "pts"), duration, samples is null ? null : (int)samples));
            }
            return OperationResult<InitialReferenceObservations>.Succeeded(new(streams, frames));
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
