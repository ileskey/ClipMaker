using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class H264PicturePayloadReaderTests
{
    [Fact]
    public void FramingAndNonPictureNalsDoNotSubstituteForDecodedEquality()
    {
        var reader = new H264PicturePayloadReader();
        var avcc = reader.Read(new byte[] { 0, 0, 0, 3, 0x65, 1, 2 }, false);
        var annex = reader.Read(new byte[] { 0, 0, 0, 1, 0x67, 3, 4, 0, 0, 1, 0x65, 1, 2, 0, 0 }, true);
        Assert.Equal(OperationStatus.Succeeded, avcc.Status);
        Assert.Equal(avcc.Data, annex.Data);
        Assert.True(avcc.Data!.ContainsIdr);
        Assert.Equal(1, avcc.Data.SliceCount);
        Assert.NotEqual(avcc.Data.Sha256, reader.Read(new byte[] { 0, 0, 0, 3, 0x65, 1, 3 }, false).Data!.Sha256);
    }
    [Theory]
    [InlineData(false, new byte[] { 0, 0, 0, 0 })]
    [InlineData(false, new byte[] { 0, 0, 0, 3, 0x65, 1 })]
    [InlineData(false, new byte[] { 0, 0, 0, 2, 0xe5, 1 })]
    [InlineData(false, new byte[] { 0, 0, 0, 2, 0x67, 1 })]
    [InlineData(true, new byte[] { 1, 2, 3 })]
    [InlineData(true, new byte[] { 1, 0, 0, 1, 0x65, 1 })]
    [InlineData(true, new byte[] { 0, 0, 1, 0x65 })]
    public void InvalidBoundsOrMissingPictureCannotProduceIdentity(bool annex, byte[] bytes)
    {
        var result = new H264PicturePayloadReader().Read(bytes, annex);
        Assert.Null(result.Data); Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
    }
}
