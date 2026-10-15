using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class InitialPacketObservationTests
{
    private static readonly string Streams = """{"streams":[{"index":0,"codec_type":"video","time_base":"1/6000"},{"index":1,"codec_type":"audio","time_base":"1/48000"}]}""".Trim('{', '}');
    private static OperationResult<InitialReferenceObservations> Parse(string entries) =>
        new InitialReferenceObservationParser().Parse(Encoding.UTF8.GetBytes("{" + Streams + ",\"packets_and_frames\":[" + entries + "]}"));
    [Fact]
    public void MixedOutputSeparatesSignedPacketClocksAndDecodedFrames()
    {
        var result = Parse("""{"type":"packet","stream_index":0,"pts":-1,"dts":-201,"duration":200,"flags":"K__"},{"type":"frame","stream_index":0,"media_type":"video","pts":-1}""");
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.True(result.Data!.PacketsObserved);
        var packet = Assert.Single(result.Data.Packets);
        Assert.Equal(-1, packet.Pts); Assert.Equal(-201, packet.Dts); Assert.Equal("K__", packet.Flags);
        Assert.Single(result.Data.Frames); Assert.Null(packet.Skip);
        Assert.True(((System.Collections.IList)result.Data.Packets).IsReadOnly);
    }
    [Fact]
    public void MissingClocksAndSideDataAreNotFilled()
    {
        var packet = Assert.Single(Parse("""{"type":"packet","stream_index":1,"best_effort_timestamp":0}""").Data!.Packets);
        Assert.Null(packet.Pts); Assert.Null(packet.Dts); Assert.Null(packet.Duration); Assert.Null(packet.Flags); Assert.Null(packet.Skip);
        Assert.False(packet.HasUninterpretedSideData);
    }
    [Fact]
    public void SkipCountsAndReasonsRemainExplicit()
    {
        var packet = Assert.Single(Parse("""{"type":"packet","stream_index":1,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":4294967295,"discard_padding":0,"skip_reason":1,"discard_reason":0}]}""").Data!.Packets);
        Assert.Equal(uint.MaxValue, packet.Skip!.SkipSamples); Assert.Equal(0u, packet.Skip.DiscardPadding);
        Assert.Equal((byte)1, packet.Skip.SkipReason); Assert.Equal((byte)0, packet.Skip.DiscardReason);
    }
    [Fact]
    public void IncompleteAndOtherSideDataArePreservedAsUncertainty()
    {
        var packet = Assert.Single(Parse("""{"type":"packet","stream_index":1,"side_data_list":[{"side_data_type":"Skip Samples"},{"side_data_type":"New Metadata"}]}""").Data!.Packets);
        Assert.NotNull(packet.Skip); Assert.Null(packet.Skip.SkipSamples); Assert.Null(packet.Skip.DiscardPadding);
        Assert.True(packet.HasUninterpretedSideData);
    }
    [Theory]
    [InlineData("{\"type\":\"other\"}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":9}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"pts\":1.5}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"dts\":9223372036854775808}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"duration\":-1}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"flags\":\"K\\nSECRET\"}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":null}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":[{\"side_data_type\":\"Skip Samples\",\"skip_samples\":-1}]}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":[{\"side_data_type\":\"Skip Samples\",\"discard_padding\":4294967296}]}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":[{\"side_data_type\":\"Skip Samples\",\"skip_reason\":256}]}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":0,\"side_data_list\":[{\"side_data_type\":\"Skip Samples\"}]}")]
    [InlineData("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":[{\"side_data_type\":\"Skip Samples\"},{\"side_data_type\":\"Skip Samples\"}]}")]
    public void MalformedPacketsNeverPublishPartialData(string entries)
    {
        var result = Parse(entries); Assert.Null(result.Data); Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
        Assert.DoesNotContain("SECRET", result.Error.Message);
    }
    [Fact]
    public void MutuallyExclusiveLayoutsAndDuplicateFieldsAreRejected()
    {
        var parser = new InitialReferenceObservationParser();
        Assert.Null(parser.Parse(Encoding.UTF8.GetBytes("{" + Streams + ",\"packets_and_frames\":[],\"frames\":[]}")).Data);
        Assert.Null(Parse("""{"type":"packet","stream_index":1,"pts":0,"pts":1}""").Data);
    }
    [Fact]
    public void LegacyFrameOnlyOutputDoesNotClaimPacketsObserved()
    {
        var data = new InitialReferenceObservationParser().Parse(Encoding.UTF8.GetBytes("{" + Streams + ",\"frames\":[]}")).Data!;
        Assert.False(data.PacketsObserved); Assert.Empty(data.Packets);
        Assert.True(Parse("").Data!.PacketsObserved);
    }
    [Fact]
    public void PacketAndSideDataCountsAreBounded()
    {
        string packet = "{\"type\":\"packet\",\"stream_index\":1}";
        Assert.Null(Parse(string.Join(',', Enumerable.Repeat(packet, InitialReferenceObservationParser.MaximumPackets + 1))).Data);
        string side = "{\"side_data_type\":\"Other\"}";
        Assert.Null(Parse("{\"type\":\"packet\",\"stream_index\":1,\"side_data_list\":[" + string.Join(',', Enumerable.Repeat(side, 65)) + "]}").Data);
    }
}
