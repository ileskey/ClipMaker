using System.IO;
using System.Windows.Media.Imaging;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Imaging;

public sealed class WpfThumbnailDecoder : IThumbnailDecoder
{
    public ThumbnailDecodeResult Decode(ReadOnlyMemory<byte> bytes, ThumbnailImageKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ThumbnailImageHeader.TryRead(bytes.Span, kind, out int width, out int height)) return ThumbnailDecodeResult.Invalid;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            // Inspect the codec's original frame dimensions as well as our bounded header preflight.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth != width || decoder.Frames[0].PixelHeight != height)
                return ThumbnailDecodeResult.Invalid;
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
            if (width > 640 || height > 360)
            {
                if ((long)width * 360 >= (long)height * 640) image.DecodePixelWidth = 640;
                else image.DecodePixelHeight = 360;
            }
            image.EndInit();
            if (image.PixelWidth <= 0 || image.PixelHeight <= 0 || image.PixelWidth > 640 || image.PixelHeight > 360 || !image.CanFreeze)
                return ThumbnailDecodeResult.Invalid;
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return new(image, null);
        }
        catch (Exception e) when (e is not OperationCanceledException && e is not OutOfMemoryException)
        { return ThumbnailDecodeResult.Invalid; }
    }
}
