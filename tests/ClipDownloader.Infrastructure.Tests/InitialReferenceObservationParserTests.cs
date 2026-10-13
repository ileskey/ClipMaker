using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class InitialReferenceObservationParserTests
{
    private const string Video = """{"index":0,"codec_type":"video","time_base":"1/6000","start_pts":12000,"r_frame_rate":"30000/1001","avg_frame_rate":"30/1"}""";
    private static OperationResult<InitialReferenceObservations> Parse(string streams = Video, string frames = "") =>
        new InitialReferenceObservationParser().Parse(Encoding.UTF8.GetBytes("{\"streams\":[" + streams + "],\"frames\":[" + frames + "]}"));

    [Fact]
    public void ExactSignedTimesAndIndependentRatesRemainDemuxObservations()
    {
        var result = Parse(frames: """{"stream_index":0,"media_type":"video","pts":-1,"duration":200}""");
        var data = Assert.IsType<InitialReferenceObservations>(result.Data);
        var stream = Assert.Single(data.Streams);
        Assert.Equal(MediaTimeBasis.Demux, data.TimeBasis);
        Assert.Equal("30000/1001", stream.DeclaredRate!.ToString());
        Assert.Equal("30/1", stream.AverageRate!.ToString());
        Assert.Equal(12000, stream.StartPts);
        Assert.True(NativeTimestamp.TryCreate(Assert.Single(data.Frames).Pts!.Value, stream.TimeBase, out var timestamp, out _));
        Assert.Equal(-1, timestamp!.PresentationTime.Numerator);
        Assert.Equal(6000, timestamp.PresentationTime.Denominator);
        Assert.IsAssignableFrom<System.Collections.IList>(data.Streams);
        Assert.True(((System.Collections.IList)data.Streams).IsReadOnly);
    }

    [Fact]
    public void MissingPtsNeverUsesBestEffortDtsOrStartPts()
    {
        var data = Parse(frames: """{"stream_index":0,"media_type":"video","best_effort_timestamp":12000,"pkt_dts":12000}""").Data!;
        Assert.Null(Assert.Single(data.Frames).Pts);
        Assert.Null(Assert.Single(data.Frames).Duration);
    }

    [Fact]
    public void MissingAndUnavailableRatesStayUnknown()
    {
        var data = Parse("""{"index":0,"codec_type":"video","time_base":"1/6000","avg_frame_rate":"0/0"}""").Data!;
        Assert.Null(data.Streams[0].StartPts);
        Assert.Null(data.Streams[0].DeclaredRate);
        Assert.Null(data.Streams[0].AverageRate);
        Assert.Empty(data.Frames);
    }

    [Fact]
    public void AudioSamplesAndStringIntegerArePreserved()
    {
        var data = Parse("""{"index":1,"codec_type":"audio","time_base":"1/48000","start_pts":"95935"}""",
            """{"stream_index":1,"media_type":"audio","pts":"95935","nb_samples":1024}""").Data!;
        Assert.Equal(95935, data.Streams[0].StartPts);
        Assert.Equal(1024, data.Frames[0].SampleCount);
    }

    [Theory]
    [InlineData("{\"index\":0,\"index\":1,\"codec_type\":\"video\",\"time_base\":\"1/6000\"}")]
    [InlineData("{\"index\":0,\"codec_type\":\"subtitle\",\"time_base\":\"1/6000\"}")]
    [InlineData("{\"index\":0,\"codec_type\":\"video\",\"time_base\":\"0/1\"}")]
    [InlineData("{\"index\":0,\"codec_type\":\"video\",\"time_base\":\"1/0\"}")]
    [InlineData("{\"index\":0,\"codec_type\":\"video\",\"time_base\":\"1/6000\",\"r_frame_rate\":\"1/0\"}")]
    [InlineData("{\"index\":0,\"codec_type\":\"video\",\"time_base\":\"1/6000\",\"start_pts\":1.5}")]
    public void InvalidStreamFailsWithoutData(string stream) => AssertFailure(Parse(stream));

    [Theory]
    [InlineData("{\"stream_index\":1,\"media_type\":\"video\",\"pts\":0}")]
    [InlineData("{\"stream_index\":0,\"media_type\":\"audio\",\"pts\":0}")]
    [InlineData("{\"stream_index\":0,\"media_type\":\"video\",\"pts\":null}")]
    [InlineData("{\"stream_index\":0,\"media_type\":\"video\",\"pts\":9223372036854775808}")]
    [InlineData("{\"stream_index\":0,\"media_type\":\"video\",\"duration\":-1}")]
    [InlineData("{\"stream_index\":0,\"media_type\":\"video\",\"nb_samples\":1024}")]
    public void InvalidFrameFailsWithoutData(string frame) => AssertFailure(Parse(frames: frame));

    [Fact]
    public void DuplicateStreamIdentityFails() => AssertFailure(Parse(Video + "," + Video));

    [Fact]
    public void IncompleteDuplicateRootAndMalformedUtf8Fail()
    {
        var parser = new InitialReferenceObservationParser();
        AssertFailure(parser.Parse(Encoding.UTF8.GetBytes("{\"streams\":[],\"streams\":[],\"frames\":[]}")));
        AssertFailure(parser.Parse(Encoding.UTF8.GetBytes("{\"streams\":[]}")));
        AssertFailure(parser.Parse(new byte[] { 0xff }));
        AssertFailure(parser.Parse(new byte[InitialReferenceObservationParser.MaximumBytes + 1]));
        AssertFailure(parser.Parse(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void FrameCountIsBounded()
    {
        string frame = "{\"stream_index\":0,\"media_type\":\"video\"}";
        AssertFailure(Parse(frames: string.Join(',', Enumerable.Repeat(frame, InitialReferenceObservationParser.MaximumFrames + 1))));
    }

    private static void AssertFailure(OperationResult<InitialReferenceObservations> result)
    {
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
        Assert.Null(result.Data);
    }
}
