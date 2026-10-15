using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class InitialCodecObservationTests
{
    private static OperationResult<InitialReferenceObservations> Parse(string stream, string frame = "") =>
        new InitialReferenceObservationParser().Parse(Encoding.UTF8.GetBytes("{\"streams\":[" + stream + "],\"frames\":[" + frame + "]}"));
    private static string Video(string fields = "") => "{\"index\":0,\"codec_type\":\"video\",\"time_base\":\"1/6000\"" + fields + "}";
    private static string Audio(string fields = "") => "{\"index\":1,\"codec_type\":\"audio\",\"time_base\":\"1/48000\"" + fields + "}";

    [Fact]
    public void VideoCodecAndParametersAreObservedWithoutDefaults()
    {
        var data = Parse(Video(",\"codec_name\":\"h264\",\"profile\":\"High 4:4:4 Predictive\",\"pix_fmt\":\"yuv444p\",\"extradata_size\":44")).Data!;
        var codec = data.Streams[0].Codec;
        Assert.Equal("h264", codec.Name); Assert.Equal("High 4:4:4 Predictive", codec.Profile);
        Assert.Equal("yuv444p", codec.PixelFormat); Assert.Equal(44, codec.ExtradataSize);
        Assert.Null(codec.SampleRate); Assert.Null(codec.Channels);
    }
    [Fact]
    public void AudioStreamAndDecodedFrameParametersRemainSeparate()
    {
        var data = Parse(Audio(",\"codec_name\":\"aac\",\"profile\":\"LC\",\"sample_fmt\":\"fltp\",\"sample_rate\":\"48000\",\"channels\":2,\"extradata_size\":0"),
            """{"stream_index":1,"media_type":"audio","pts":95935,"nb_samples":1024,"sample_fmt":"s16","sample_rate":44100,"channels":1}""").Data!;
        Assert.Equal(48000, data.Streams[0].Codec.SampleRate); Assert.Equal(2, data.Streams[0].Codec.Channels);
        Assert.Equal("fltp", data.Streams[0].Codec.SampleFormat); Assert.Equal(0, data.Streams[0].Codec.ExtradataSize);
        var frame = Assert.Single(data.Frames);
        Assert.Equal(44100, frame.SampleRate); Assert.Equal(1, frame.Channels); Assert.Equal("s16", frame.SampleFormat);
        Assert.Equal(1024, frame.SampleCount);
    }
    [Fact]
    public void MissingUnknownParametersAreNotGuessedFromClockOrCodec()
    {
        var data = Parse(Audio(",\"codec_name\":\"aac\",\"profile\":\"unknown\",\"sample_fmt\":\"N/A\""),
            """{"stream_index":1,"media_type":"audio","pts":95935,"nb_samples":1024}""").Data!;
        Assert.Null(data.Streams[0].Codec.Profile); Assert.Null(data.Streams[0].Codec.SampleFormat);
        Assert.Null(data.Streams[0].Codec.SampleRate); Assert.Null(data.Streams[0].Codec.Channels);
        Assert.Null(data.Streams[0].Codec.ExtradataSize); Assert.Null(data.Frames[0].SampleRate);
        Assert.Null(Parse(Video()).Data!.Streams[0].Codec.Name);
    }
    [Theory]
    [InlineData(",\"sample_rate\":0")]
    [InlineData(",\"sample_rate\":-1")]
    [InlineData(",\"sample_rate\":1.5")]
    [InlineData(",\"channels\":2147483648")]
    [InlineData(",\"channels\":0")]
    [InlineData(",\"extradata_size\":-1")]
    [InlineData(",\"codec_name\":null")]
    [InlineData(",\"profile\":23")]
    [InlineData(",\"sample_fmt\":\"fltp\\nSECRET\"")]
    [InlineData(",\"pix_fmt\":\"yuv420p\"")]
    public void MalformedAudioParametersFailWithoutData(string fields)
    {
        var result = Parse(Audio(fields));
        Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.Data);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
        Assert.DoesNotContain("SECRET", result.Error.Message);
    }
    [Theory]
    [InlineData(",\"sample_rate\":48000")]
    [InlineData(",\"channels\":2")]
    [InlineData(",\"sample_fmt\":\"fltp\"")]
    public void AudioParametersOnVideoAreNotAccepted(string fields) => Assert.Null(Parse(Video(fields)).Data);
    [Fact]
    public void LabelSizeAndDuplicateFieldsAreBounded()
    {
        Assert.Null(Parse(Video(",\"profile\":\"" + new string('x', 129) + "\"")).Data);
        Assert.Null(Parse(Audio(",\"sample_rate\":48000,\"sample_rate\":44100")).Data);
    }
    [Theory]
    [InlineData("\"sample_rate\":0")]
    [InlineData("\"channels\":-1")]
    [InlineData("\"sample_fmt\":null")]
    public void MalformedFrameParametersFail(string field) => Assert.Null(Parse(Audio(),
        "{\"stream_index\":1,\"media_type\":\"audio\"," + field + "}").Data);
}
