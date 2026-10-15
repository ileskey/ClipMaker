using System.Diagnostics.CodeAnalysis;

namespace ClipDownloader.Application.Models;

public enum VideoPlatform { Chzzk, YouTube }

// Canonical identity only. A valid reference does not imply that the video exists or is accessible.
public sealed record VideoSourceReference
{
    public VideoPlatform Platform { get; }
    public string Id { get; }
    public Uri PageUrl { get; }

    private VideoSourceReference(VideoPlatform platform, string id, Uri pageUrl)
    {
        Platform = platform;
        Id = id;
        PageUrl = pageUrl;
    }

    public static bool TryCreate(VideoPlatform platform, string? id,
        [NotNullWhen(true)] out VideoSourceReference? value)
    {
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        value = null;
        if (string.IsNullOrEmpty(id)) return false;
        bool valid = platform == VideoPlatform.Chzzk
            ? id.All(char.IsAsciiDigit)
            : id.Length == 11 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
        if (!valid) return false;
        string canonical = platform == VideoPlatform.Chzzk ? $"https://chzzk.naver.com/video/{id}" :
            $"https://www.youtube.com/watch?v={id}";
        if (!Uri.TryCreate(canonical, UriKind.Absolute, out var pageUrl)) return false;
        value = new(platform, id, pageUrl);
        return true;
    }
}
