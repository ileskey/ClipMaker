using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsPresentationGroupReaderTests
{
    private const string Master = "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"234\",NAME=\"audio\",URI=\"a/itag/234/index.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,CODECS=\"avc1.640028,mp4a.40.2\",AUDIO=\"234\"\nv/itag/270/index.m3u8\n";
    [Fact]
    public void ActualGroupDeclarationUsesRepresentationAndExplicitRendition()
    {
        var result = new HlsPresentationGroupReader().Read(Encoding.UTF8.GetBytes(Master), new("https://media.example/master.m3u8"), "270", "234");
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal("234", result.Data!.GroupId);
        Assert.Equal("https://media.example/v/itag/270/index.m3u8", result.Data.Video.AbsoluteUri);
        Assert.Equal("https://media.example/a/itag/234/index.m3u8", result.Data.Audio.AbsoluteUri);
        Assert.DoesNotContain("https", result.Data.ToString()!);
    }
    [Theory]
    [InlineData("mismatch")]
    [InlineData("duplicate")]
    [InlineData("duplicateattr")]
    [InlineData("duplicaterepresentation")]
    [InlineData("http")]
    [InlineData("missing")]
    [InlineData("quote")]
    [InlineData("trailing")]
    [InlineData("utf8")]
    [InlineData("cap")]
    public void AmbiguousUnsafeAndMalformedDeclarationsCannotProveAGroup(string fault)
    {
        string text = fault switch
        {
            "mismatch" => Master.Replace("AUDIO=\"234\"", "AUDIO=\"other\""),
            "duplicate" => Master + "#EXT-X-STREAM-INF:AUDIO=\"234\"\nother/itag/270/x.m3u8\n",
            "duplicateattr" => Master.Replace("NAME=\"audio\"", "GROUP-ID=\"234\""),
            "duplicaterepresentation" => Master.Replace("v/itag/270/index.m3u8", "v/itag/270/index.m3u8?itag=270"),
            "http" => Master.Replace("v/itag/270/index.m3u8", "http://media.example/itag/270/x"),
            "missing" => Master.Replace("a/itag/234", "a/itag/999"),
            "quote" => Master.Replace("AUDIO=\"234\"", "AUDIO=\"234"),
            "trailing" => Master.Replace("AUDIO=\"234\"", "AUDIO=\"234\","),
            _ => Master
        };
        var bytes = fault == "utf8" ? new byte[] { 255 } : fault == "cap" ? new byte[512 * 1024 + 1] : Encoding.UTF8.GetBytes(text);
        var result = new HlsPresentationGroupReader().Read(bytes, new("https://media.example/master.m3u8"), "270", "234");
        Assert.Null(result.Data);
        Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
        Assert.Equal("ProviderPresentationGroup", result.Error.Field);
        Assert.DoesNotContain("https", result.Error.Message);
    }
}
