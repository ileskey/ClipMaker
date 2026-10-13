namespace ClipMaker.Media.Core.Models;

// Captured local byte content; this does not identify a remote asset version.
public sealed record MediaFileContentIdentity
{
    public long Bytes { get; }
    public string Sha256 { get; }
    public MediaFileContentIdentity(long bytes, string sha256)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("A SHA256 byte identity is required.", nameof(sha256));
        Bytes = bytes;
        Sha256 = sha256.ToLowerInvariant();
    }
}
