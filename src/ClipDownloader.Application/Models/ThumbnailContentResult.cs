namespace ClipDownloader.Application.Models;

public enum ThumbnailContentStatus { Succeeded, Failed, Cancelled }
public enum ThumbnailImageKind { Jpeg, Png }
public enum ThumbnailFailure { UnsafeUri, DestinationRejected, RedirectRejected, TimedOut, HttpFailure, UnsupportedImage, TooLarge, InvalidImage }

public sealed class ThumbnailContentResult
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public ThumbnailContentStatus Status { get; }
    public ReadOnlyMemory<byte> Content { get; }
    public ThumbnailImageKind? Kind { get; }
    public ThumbnailFailure? Failure { get; }
    private ThumbnailContentResult(ThumbnailContentStatus status, ReadOnlyMemory<byte> content = default,
        ThumbnailImageKind? kind = null, ThumbnailFailure? failure = null)
    { Status = status; Content = content; Kind = kind; Failure = failure; }

    public static ThumbnailContentResult Success(ReadOnlySpan<byte> content, ThumbnailImageKind kind)
    {
        if (content.Length is 0 or > MaximumBytes || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid bounded image content.");
        return new(ThumbnailContentStatus.Succeeded, content.ToArray(), kind);
    }
    public static ThumbnailContentResult Failed(ThumbnailFailure failure) => new(ThumbnailContentStatus.Failed, failure: failure);
    public static ThumbnailContentResult Cancelled() => new(ThumbnailContentStatus.Cancelled);
}
