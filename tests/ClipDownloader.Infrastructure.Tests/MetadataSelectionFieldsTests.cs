using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.YtDlp;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class MetadataSelectionFieldsTests
{
    private static VideoInfo Parse(string value, string protocol = "https")
    {
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        string json = "{\"id\":\"123\",\"formats\":[{\"format_id\":\"v\",\"vcodec\":\"avc1\",\"acodec\":\"aac\",\"vbr\":" + value + ",\"abr\":" + value + ",\"protocol\":\"" + protocol + "\"}]}";
        return Assert.IsType<VideoInfo>(new YtDlpMetadataParser().Parse(Encoding.UTF8.GetBytes(json), source!).Data);
    }
    [Theory]
    [InlineData("129.501", "129.501")]
    [InlineData("1.29501e2", "129.501")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("1e-28", "0.0000000000000000000000000001")]
    public void BitrateIsExactWithinDecimalStorage(string json, string expected)
    {
        decimal number = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var format = Assert.Single(Parse(json).Formats);
        Assert.Equal(number, format.VideoBitrateKbps); Assert.Equal(number, format.AudioBitrateKbps);
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"129.501\"")]
    [InlineData("1e-29")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("1e999999999999999999999")]
    [InlineData("1e-999999999999999999999")]
    [InlineData("1.23456789012345678901234567891")]
    public void UnrepresentableBitrateIsNotRoundedOrAssumed(string json)
    {
        var info = Parse(json);
        Assert.Null(info.Formats[0].VideoBitrateKbps); Assert.Null(info.Formats[0].AudioBitrateKbps);
        Assert.Contains(info.Issues, i => i.Field == "vbr");
    }
    [Theory]
    [InlineData("https", FormatTransport.Https)]
    [InlineData("m3u8_native", FormatTransport.Hls)]
    [InlineData("m3u8", FormatTransport.Hls)]
    [InlineData("http_dash_segments", FormatTransport.Other)]
    public void ProtocolHasNoMediaUrlInThePublicModel(string protocol, FormatTransport expected) => Assert.Equal(expected, Parse("1", protocol).Formats[0].Transport);
}
