using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDownloader.Application.Models;
using ClipDownloader.Imaging;
using Xunit;

namespace ClipDownloader.Tests;

public sealed class ThumbnailDecodeTests
{
    internal static byte[] Image(int width, int height, bool jpeg = false)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[width * height * 4], width * 4);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    [Theory]
    [InlineData(16, 9, false, true)] [InlineData(16, 9, true, true)]
    [InlineData(1920, 1080, false, true)] [InlineData(1080, 1920, true, true)]
    [InlineData(4096, 1, false, true)] [InlineData(1, 4096, false, true)]
    [InlineData(4000, 2000, false, true)] [InlineData(4000, 2001, false, false)]
    [InlineData(4097, 1, false, false)] [InlineData(1, 4097, false, false)]
    public void DimensionsFrozenPixelsAndAspect(int width, int height, bool jpeg, bool valid)
    {
        var result = new WpfThumbnailDecoder().Decode(Image(width, height, jpeg), jpeg ? ThumbnailImageKind.Jpeg : ThumbnailImageKind.Png,
            TestContext.Current.CancellationToken);
        Assert.Equal(valid, result.Image is not null);
        if (result.Image is { } image)
        {
            Assert.True(image.IsFrozen); Assert.InRange(image.PixelWidth, 1, 640); Assert.InRange(image.PixelHeight, 1, 360);
            Assert.True(Math.Abs(image.PixelHeight - image.PixelWidth * (double)height / width) <= 1 ||
                Math.Abs(image.PixelWidth - image.PixelHeight * (double)width / height) <= 1);
            image.CopyPixels(new byte[image.PixelWidth * image.PixelHeight * 4], image.PixelWidth * 4, 0);
        }
    }

    [Fact]
    public void TruncatedCorruptAnimatedAndOverflowRejected()
    {
        var decoder = new WpfThumbnailDecoder(); byte[] normal = Image(16, 9);
        foreach (int length in new[] { 0, 7, 24, normal.Length - 1 })
            Assert.Null(decoder.Decode(normal.AsMemory(0, length), ThumbnailImageKind.Png, TestContext.Current.CancellationToken).Image);
        var overflow = normal.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(overflow.AsSpan(16), uint.MaxValue);
        Assert.Null(decoder.Decode(overflow, ThumbnailImageKind.Png, TestContext.Current.CancellationToken).Image);
        var animation = new byte[normal.Length + 20]; normal.AsSpan(0, 33).CopyTo(animation);
        BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(33), 8); "acTL"u8.CopyTo(animation.AsSpan(37)); normal.AsSpan(33).CopyTo(animation.AsSpan(53));
        Assert.Null(decoder.Decode(animation, ThumbnailImageKind.Png, TestContext.Current.CancellationToken).Image);
        var corrupt = normal.ToArray(); corrupt[50] ^= 255;
        Assert.Null(decoder.Decode(corrupt, ThumbnailImageKind.Png, TestContext.Current.CancellationToken).Image);
        var jpeg = Image(32, 18, true); jpeg[^1] = 0;
        Assert.Null(decoder.Decode(jpeg, ThumbnailImageKind.Jpeg, TestContext.Current.CancellationToken).Image);
        Assert.Null(decoder.Decode(normal, ThumbnailImageKind.Jpeg, TestContext.Current.CancellationToken).Image);
    }

    [Fact]
    public void CancellationBeforeDecodeDoesNotProduceImage()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new WpfThumbnailDecoder().Decode(Image(2, 2), ThumbnailImageKind.Png, cts.Token));
    }
}
