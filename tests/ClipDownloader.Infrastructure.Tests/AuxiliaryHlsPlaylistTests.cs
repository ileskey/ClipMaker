using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AuxiliaryHlsPlaylistTests
{
    private const string Text = "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:6\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:6.000,\na.ts\n#EXTINF:5.125,\nb.ts\n#EXT-X-ENDLIST\n";
    [Fact]
    public void StandaloneReferencesHaveExactPlaylistCoordinatesWithoutAnInitialization()
    {
        var result = new AuxiliaryHlsPlaylistParser().Parse(Encoding.UTF8.GetBytes(Text), new("https://media.example/leaf.m3u8"));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(F.Time(89, 8), result.Data!.Duration);
        Assert.Equal(F.Range(0, 6), result.Data.Segments[0].PlaylistRange);
        Assert.Equal(1, result.Data.Segments[1].Sequence);
        Assert.Equal("https://media.example/a.ts", result.Data.Segments[0].Uri.AbsoluteUri);
        Assert.True(((System.Collections.IList)result.Data.Segments).IsReadOnly);
    }
    [Theory]
    [InlineData("map")]
    [InlineData("encrypted")]
    [InlineData("range")]
    [InlineData("discontinuity")]
    [InlineData("live")]
    [InlineData("master")]
    public void ReferenceStrategyRejectsOtherInitializationAndTimelineLayouts(string kind)
    {
        string text = kind switch
        {
            "map" => Text.Replace("#EXTINF:6.000,", "#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6.000,"),
            "encrypted" => Text.Replace("#EXTINF:6.000,", "#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n#EXTINF:6.000,"),
            "range" => Text.Replace("#EXTINF:6.000,", "#EXT-X-BYTERANGE:10@0\n#EXTINF:6.000,"),
            "discontinuity" => Text.Replace("#EXTINF:6.000,", "#EXT-X-DISCONTINUITY\n#EXTINF:6.000,"),
            "live" => Text.Replace("#EXT-X-ENDLIST\n", ""),
            _ => "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\nleaf.m3u8\n"
        };
        var result = new AuxiliaryHlsPlaylistParser().Parse(Encoding.UTF8.GetBytes(text), new("https://media.example/leaf.m3u8"));
        Assert.Null(result.Data);
        Assert.Equal(OperationStatus.Failed, result.Status);
    }
    [Fact]
    public void Fmp4SelectedParserStillRequiresInitialization()
    {
        Assert.Equal(OperationErrorCode.UnsupportedFormat, new HlsMediaPlaylistParser().Parse(
            Encoding.UTF8.GetBytes(Text.Replace("VERSION:3", "VERSION:7")), new("https://media.example/leaf.m3u8")).Error!.Code);
    }
}
