using System.Text;
using System.Text.Json.Nodes;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class H264CarrierObservationParserTests
{
    private const string Json = """{"streams":[{"index":0,"codec_type":"video","codec_name":"h264","time_base":"1/90000"}],"packets":[{"stream_index":0,"pts":3000,"size":"6","flags":"K__","data":"\n00000000: 0000 0165 0102  ...e..\n"}]}""";
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTimestampIsNotSynthesizedFromPacketContent(bool missing)
    {
        var node = JsonNode.Parse(Json)!;
        if (missing) node["packets"]![0]!.AsObject().Remove("pts");
        var result = new H264CarrierObservationParser().Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var picture = Assert.Single(result.Data!.Pictures);
        Assert.Equal(missing ? null : (long?)3000, picture.Pts);
        Assert.True(picture.Payload.ContainsIdr);
        Assert.True(((System.Collections.IList)result.Data.Pictures).IsReadOnly);
    }
    [Theory]
    [InlineData("clock")]
    [InlineData("corrupt")]
    [InlineData("size")]
    [InlineData("offset")]
    [InlineData("duplicate")]
    [InlineData("stream")]
    [InlineData("empty")]
    public void UnsafeIncompleteAndAmbiguousPacketDataCannotPublishObservations(string fault)
    {
        var node = JsonNode.Parse(Json)!;
        switch (fault)
        {
            case "clock": node["streams"]![0]!["time_base"] = "1/6000"; break;
            case "corrupt": node["packets"]![0]!["flags"] = "KC_"; break;
            case "size": node["packets"]![0]!["size"] = "7"; break;
            case "offset": node["packets"]![0]!["data"] = "\n00000001: 0000 0165 0102  ...e..\n"; break;
            case "stream": node["packets"]![0]!["stream_index"] = 1; break;
            case "empty": node["packets"]!.AsArray().Clear(); break;
        }
        string json = fault == "duplicate" ? Json.Replace("\"pts\":3000", "\"pts\":3000,\"pts\":3000") : node.ToJsonString();
        var result = new H264CarrierObservationParser().Parse(Encoding.UTF8.GetBytes(json));
        Assert.Null(result.Data); Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
    }
}
