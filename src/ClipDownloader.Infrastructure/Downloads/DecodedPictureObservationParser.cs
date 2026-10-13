using System.Globalization;
using System.Text;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record DecodedPictureObservation(long Pts, long Duration, int Bytes, string Sha256);
internal sealed class DecodedPictureObservations(SignedMediaTime clock, int width, int height, IEnumerable<DecodedPictureObservation> pictures)
{
    internal SignedMediaTime Clock { get; } = clock;
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal IReadOnlyList<DecodedPictureObservation> Pictures { get; } = Array.AsReadOnly(pictures.ToArray());
}

internal sealed class DecodedPictureObservationParser
{
    internal OperationResult<DecodedPictureObservations> Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (bytes.Length is < 1 or > 1024 * 1024) throw new InvalidDataException();
            string text = new UTF8Encoding(false, true).GetString(bytes.Span);
            var lines = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length != 0).ToArray();
            if (lines.Length > 8192) throw new InvalidDataException();
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            var pictures = new List<DecodedPictureObservation>();
            foreach (string line in lines)
            {
                if (line.Length > 4096) throw new InvalidDataException();
                if (line.StartsWith('#'))
                {
                    int colon = line.IndexOf(':');
                    if (colon >= 0 && !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) throw new InvalidDataException();
                    continue;
                }
                var fields = line.Split(',').Select(s => s.Trim()).ToArray();
                if (fields.Length != 6 || fields[0] != "0" || pictures.Count == 4096) throw new InvalidDataException();
                long dts = Integer(fields[1]), pts = Integer(fields[2]), duration = Integer(fields[3]);
                int size = checked((int)Integer(fields[4])); string hash = fields[5];
                if (dts != pts || duration <= 0 || size <= 0 || hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException();
                if (pictures.Count != 0 && pts <= pictures[^1].Pts) throw new InvalidDataException();
                pictures.Add(new(pts, duration, size, hash.ToUpperInvariant()));
            }
            if (pictures.Count == 0 || headers.GetValueOrDefault("#format") != "frame checksums" || headers.GetValueOrDefault("#version") != "2" ||
                headers.GetValueOrDefault("#hash") != "SHA256" || headers.GetValueOrDefault("#media_type 0") != "video" ||
                headers.GetValueOrDefault("#codec_id 0") != "rawvideo") throw new InvalidDataException();
            foreach (string prefix in new[] { "#tb ", "#media_type ", "#codec_id ", "#dimensions " })
                if (headers.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal) && k != prefix + "0")) throw new InvalidDataException();
            var clockText = headers["#tb 0"].Split('/');
            if (clockText.Length != 2 || !SignedMediaTime.TryCreate(Integer(clockText[0]), Integer(clockText[1]), out var clock, out _) || clock.Numerator <= 0)
                throw new InvalidDataException();
            var dimensions = headers["#dimensions 0"].Split('x');
            if (dimensions.Length != 2) throw new InvalidDataException();
            int width = checked((int)Integer(dimensions[0])), height = checked((int)Integer(dimensions[1]));
            if (width is < 1 or > 8192 || height is < 1 or > 8192 || width % 2 != 0 || height % 2 != 0) throw new InvalidDataException();
            long expectedSize = (long)width * height * 3 / 2; // Caller requests yuv420p explicitly.
            if (pictures.Any(p => p.Bytes != expectedSize)) throw new InvalidDataException();
            return OperationResult<DecodedPictureObservations>.Succeeded(new(clock, width, height, pictures));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException or OverflowException)
        { return OperationResult<DecodedPictureObservations>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "제한된 decoded picture 관찰을 확인하지 못했습니다.", "DecodedPictureObservation")); }
    }
    private static long Integer(string text)
    {
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)) throw new InvalidDataException();
        return value;
    }
}
