using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.YtDlp;

internal static class MetadataNumbers
{
    private readonly record struct DecimalParts(string Digits, long Power);

    private static bool TryPositiveParts(JsonElement value, out DecimalParts parts)
    {
        parts = default;
        if (value.ValueKind != JsonValueKind.Number) return false;
        string raw = value.GetRawText();
        if (raw[0] == '-') return false;
        int exponentAt = raw.IndexOfAny(['e', 'E']);
        string mantissa = exponentAt < 0 ? raw : raw[..exponentAt];
        long exponent = 0;
        if (exponentAt >= 0 && !long.TryParse(raw.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out exponent))
            exponent = raw[exponentAt + 1] == '-' ? long.MinValue : long.MaxValue;
        int dot = mantissa.IndexOf('.');
        int fractionalDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return false;
        int beforeTrim = digits.Length;
        digits = digits.TrimEnd('0');
        int trailingZeros = beforeTrim - digits.Length;
        long power;
        try { power = checked(exponent - fractionalDigits + trailingZeros); }
        catch (OverflowException) { power = exponent < 0 ? long.MinValue : long.MaxValue; }
        parts = new(digits, power);
        return true;
    }

    public static FrameRate? FrameRate(JsonElement value)
    {
        if (!TryPositiveParts(value, out var parts)) return null;
        // With trailing decimal zeros removed, at least 2^scale remains in a denominator.
        // A positive Int64 denominator therefore cannot need a decimal scale above 62.
        if (parts.Power < -62 || parts.Power > 18) return null;
        int scale = (int)Math.Max(0, -parts.Power);
        if (parts.Digits.Length > 19 + scale) return null;
        BigInteger numerator = BigInteger.Parse(parts.Digits, CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.One;
        if (parts.Power >= 0) numerator *= BigInteger.Pow(10, (int)parts.Power);
        else denominator = BigInteger.Pow(10, scale);
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;
        if (numerator > long.MaxValue || denominator > long.MaxValue) return null;
        return ClipMaker.Media.Core.ValueObjects.FrameRate.TryCreate((long)numerator, (long)denominator, out var rate, out _) ? rate : null;
    }

    public static int? Dimension(JsonElement value)
    {
        if (!TryPositiveParts(value, out var parts) || parts.Power < 0 || parts.Power > 9 ||
            parts.Digits.Length + parts.Power > 10) return null;
        long result = long.Parse(parts.Digits, CultureInfo.InvariantCulture);
        for (int count = 0; count < parts.Power; count++) result *= 10;
        return result <= int.MaxValue ? (int)result : null;
    }

    public static decimal? Bitrate(JsonElement value)
    {
        if (!TryPositiveParts(value, out var parts) || parts.Power < -28 || parts.Power > 28 ||
            parts.Digits.Length > 29 || parts.Power >= 0 && parts.Digits.Length + parts.Power > 29) return null;
        BigInteger coefficient = BigInteger.Parse(parts.Digits, CultureInfo.InvariantCulture);
        if (parts.Power > 0) coefficient *= BigInteger.Pow(10, (int)parts.Power);
        if (coefficient >= BigInteger.One << 96) return null;
        uint low = (uint)(coefficient & uint.MaxValue);
        uint middle = (uint)((coefficient >> 32) & uint.MaxValue);
        uint high = (uint)(coefficient >> 64);
        return new decimal(unchecked((int)low), unchecked((int)middle), unchecked((int)high), false,
            (byte)Math.Max(0, -parts.Power));
    }

    public static TimeSpan? Duration(JsonElement value, out bool rounded)
    {
        rounded = false;
        if (!TryPositiveParts(value, out var parts)) return null;
        long tickPower = parts.Power > long.MaxValue - 7 ? long.MaxValue : parts.Power + 7;
        if (tickPower >= 0)
        {
            if (tickPower > 18 || parts.Digits.Length + tickPower > 19) return null;
            BigInteger ticks = BigInteger.Parse(parts.Digits, CultureInfo.InvariantCulture) * BigInteger.Pow(10, (int)tickPower);
            return ticks <= long.MaxValue ? TimeSpan.FromTicks((long)ticks) : null;
        }
        long integerLength = parts.Digits.Length + tickPower;
        if (integerLength > 19) return null;
        if (integerLength < 0) { rounded = true; return TimeSpan.Zero; }
        int length = (int)integerLength;
        ulong integer = length == 0 ? 0 : ulong.Parse(parts.Digits.AsSpan(0, length), CultureInfo.InvariantCulture);
        // Exact values above TimeSpan.MaxValue must not be hidden by rounding down.
        if (integer >= (ulong)long.MaxValue) return null;
        rounded = true;
        char next = parts.Digits[length];
        bool moreThanHalf = next > '5' || next == '5' && parts.Digits.AsSpan(length + 1).IndexOfAnyExcept('0') >= 0;
        if (moreThanHalf || next == '5' && (integer & 1) != 0) integer++;
        return TimeSpan.FromTicks((long)integer);
    }
}
