using System.Windows.Media.Imaging;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Imaging;

public sealed record ThumbnailDecodeResult(BitmapSource? Image, ThumbnailFailure? Failure)
{
    public static ThumbnailDecodeResult Invalid { get; } = new(null, ThumbnailFailure.InvalidImage);
}

public interface IThumbnailDecoder
{
    ThumbnailDecodeResult Decode(ReadOnlyMemory<byte> bytes, ThumbnailImageKind kind, CancellationToken cancellationToken);
}
