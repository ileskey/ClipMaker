using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.YtDlp;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class YtDlpMetadataParserTests
{
    private static OperationResult<VideoInfo> Parse(string json)
    {
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        return new YtDlpMetadataParser().Parse(Encoding.UTF8.GetBytes(json), source!);
    }
    private static VideoInfo Info(string fields) => Assert.IsType<VideoInfo>(Parse("{\"id\":\"123\"," + fields + "}").Data);

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"id\":123}")]
    [InlineData("{\"id\":\"456\"}")]
    [InlineData("{\"id\":\"123\",\"id\":\"123\"}")]
    [InlineData("{\"id\":\"123\",\"_type\":\"playlist\"}")]
    [InlineData("{\"id\":\"123\",\"entries\":[]}")]
    [InlineData("{\"id\":\"123\",\"formats\":[],\"formats\":[]}")]
    [InlineData("{\"id\":\"123\"} {}")]
    [InlineData("{\"id\":\"123\"")]
    public void RejectsInvalidIdentityOrEnvelope(string json) => Assert.Equal(OperationErrorCode.InvalidMetadata, Parse(json).Error!.Code);

    [Theory]
    [InlineData("29.97", 2997L, 100L)]
    [InlineData("2.997e1", 2997L, 100L)]
    [InlineData("60.0", 60L, 1L)]
    [InlineData("0.000000000000000001", 1L, 1000000000000000000L)]
    [InlineData("9223372036854775807", long.MaxValue, 1L)]
    public void FpsPreservesExactJsonDecimal(string value, long numerator, long denominator)
    {
        var format = Assert.Single(Info("\"formats\":[{\"format_id\":\"v\",\"fps\":" + value + "}]").Formats);
        Assert.Equal(numerator, format.FrameRate!.Numerator);
        Assert.Equal(denominator, format.FrameRate.Denominator);
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("null")]
    [InlineData("\"30\"")]
    [InlineData("1e100000000000000000000")]
    [InlineData("1e-100000000000000000000")]
    [InlineData("9223372036854775808")]
    [InlineData("0.0000000000000000001")]
    public void InvalidFpsRemainsUnknown(string value) => Assert.Null(Assert.Single(Info("\"formats\":[{\"format_id\":\"v\",\"fps\":" + value + "}]").Formats).FrameRate);

    [Theory]
    [InlineData("1.25", 12500000L)]
    [InlineData("0.00000005", 0L)]
    [InlineData("0.00000015", 2L)]
    [InlineData("0.00000025", 2L)]
    [InlineData("1e-999999999999999999999", 0L)]
    [InlineData("922337203685.4775807", long.MaxValue)]
    public void DurationRoundsToEvenTicksWithoutFloatingPoint(string value, long ticks) => Assert.Equal(ticks, Info("\"duration\":" + value).Duration!.Value.Ticks);
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"1.25\"")]
    [InlineData("922337203685.47758071")]
    [InlineData("1e999999999999999999999")]
    public void InvalidDurationRemainsUnknown(string value) => Assert.Null(Info("\"duration\":" + value).Duration);

    [Fact]
    public void RemovesAllDuplicateIdsDrmAndNonMediaButPreservesUnknownStreams()
    {
        var info = Info("\"formats\":[{},null,{\"format_id\":\"a\"},{\"format_id\":\"a\"},{\"format_id\":\"drm\",\"has_drm\":true},{\"format_id\":\"story\",\"vcodec\":\"none\",\"acodec\":\"none\"},{\"format_id\":\"unknown\"},{\"format_id\":\"audio\",\"vcodec\":\"none\",\"acodec\":\"aac\"}]");
        Assert.Equal(new[] { "unknown", "audio" }, info.Formats.Select(f => f.Id));
        Assert.Equal(StreamPresence.Unknown, info.Formats[0].VideoStream);
        Assert.Equal(StreamPresence.Absent, info.Formats[1].VideoStream);
        Assert.Equal(StreamPresence.Present, info.Formats[1].AudioStream);
        Assert.Equal(2, info.Issues.Count(i => i.Reason == MetadataIssueReason.DuplicateFormat));
    }
    [Theory]
    [InlineData("not_live", VideoState.Completed)]
    [InlineData("was_live", VideoState.Completed)]
    [InlineData("is_live", VideoState.Live)]
    [InlineData("is_upcoming", VideoState.Upcoming)]
    [InlineData("post_live", VideoState.Unknown)]
    public void StateIsExplicit(string status, VideoState expected) => Assert.Equal(expected, Info("\"live_status\":\"" + status + "\"").State);
    [Fact]
    public void OptionalValuesAreConservativeAndSensitiveUrlsAreOmitted()
    {
        var info = Info("\"title\":42,\"channel\":\"anonymous\",\"upload_date\":\"20260230\",\"thumbnail\":\"https://example.org/image?token=SECRET\",\"live_status\":\"not_live\",\"is_live\":true,\"formats\":[{\"format_id\":\"v\",\"width\":1920.0,\"height\":1080.1,\"url\":\"https://example.org/?signature=SECRET\"}]");
        Assert.Null(info.Title); Assert.Equal("anonymous", info.Uploader); Assert.Null(info.UploadDate);
        Assert.Null(info.ThumbnailUrl); Assert.Equal(VideoState.Unknown, info.State);
        Assert.Equal(1920, info.Formats[0].Width); Assert.Null(info.Formats[0].Height);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(info));
    }
    [Fact]
    public void EmptyFormatsIsValidInfoAndDrmSourceFails()
    {
        Assert.Empty(Info("\"formats\":[]").Formats);
        Assert.Equal(OperationErrorCode.SourceUnavailable, Parse("{\"id\":\"123\",\"has_drm\":true}").Error!.Code);
    }
    [Fact]
    public void DuplicateDrmIsNeverLastValueWins() => Assert.Equal(OperationErrorCode.InvalidMetadata,
        Parse("{\"id\":\"123\",\"has_drm\":true,\"has_drm\":false}").Error!.Code);
    [Fact]
    public void InvalidUtf8AndOversizeFail()
    {
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        var parser = new YtDlpMetadataParser();
        Assert.Equal(OperationErrorCode.InvalidMetadata, parser.Parse(new byte[] { 0xff }, source!).Error!.Code);
        byte[] invalidString = Encoding.UTF8.GetBytes("{\"id\":\"123\",\"title\":\"x\"}");
        invalidString[Array.IndexOf(invalidString, (byte)'x')] = 0xff;
        Assert.Equal(OperationErrorCode.InvalidMetadata, parser.Parse(invalidString, source!).Error!.Code);
        Assert.Equal(OperationErrorCode.InvalidMetadata, parser.Parse(new byte[32 * 1024 * 1024 + 1], source!).Error!.Code);
    }

    [Theory]
    [InlineData("chzzk", "15430558", 2667, 5)]
    [InlineData("youtube", "sFXPM_sfJNo", 226, 43)]
    public void ParsesSanitizedActualSiteFixtures(string provider, string id, int seconds, int count)
    {
        VideoSourceReference.TryCreate(provider == "chzzk" ? VideoPlatform.Chzzk : VideoPlatform.YouTube, id, out var source);
        var result = new YtDlpMetadataParser().Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", provider + ".json")), source!);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(seconds), result.Data!.Duration);
        Assert.Equal(VideoState.Completed, result.Data.State);
        Assert.Equal(count, result.Data.Formats.Count);
        Assert.Contains(result.Data.Formats, f => f.VideoStream == StreamPresence.Present && f.FrameRate is not null);
    }
}
