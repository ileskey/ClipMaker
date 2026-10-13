using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class DecodedPictureObservationParserTests
{
    internal static string Text => "#format: frame checksums\n#version: 2\n#hash: SHA256\n#tb 0: 1/90000\n#media_type 0: video\n#codec_id 0: rawvideo\n#dimensions 0: 2x2\n0, 0, 0, 3000, 6, " + new string('A', 64) + "\n";
    [Fact]
    public void ExactClockDimensionsAndPictureRowsRemainObservations()
    {
        var result = new DecodedPictureObservationParser().Parse(Encoding.UTF8.GetBytes(Text));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(F.Time(1, 90000), result.Data!.Clock);
        Assert.Equal(2, result.Data.Width); Assert.Equal(2, result.Data.Height);
        Assert.Equal(new DecodedPictureObservation(0, 3000, 6, new string('A', 64)), Assert.Single(result.Data.Pictures));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("otherstream")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("duration")]
    [InlineData("clock")]
    [InlineData("dimensions")]
    [InlineData("dts")]
    [InlineData("order")]
    public void IncompleteOrContradictoryDecodedRowsCannotBePublished(string fault)
    {
        string text = fault switch
        {
            "duplicate" => Text + "#tb 0: 1/90000\n",
            "otherstream" => Text + "#tb 1: 1/90000\n",
            "size" => Text.Replace("3000, 6,", "3000, 5,"),
            "hash" => Text.Replace(new string('A', 64), "bad"),
            "duration" => Text.Replace("3000, 6,", "0, 6,"),
            "clock" => Text.Replace("1/90000", "1/0"),
            "dimensions" => Text.Replace("2x2", "3x2"),
            "dts" => Text.Replace("0, 0, 0, 3000", "0, -1, 0, 3000"),
            _ => Text + "0, 0, 0, 3000, 6, " + new string('A', 64) + "\n"
        };
        var result = new DecodedPictureObservationParser().Parse(Encoding.UTF8.GetBytes(text));
        Assert.Null(result.Data); Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
    }
}
