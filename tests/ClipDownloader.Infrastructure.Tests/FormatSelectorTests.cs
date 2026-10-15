using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class FormatSelectorTests
{
    private static FrameRate Rate(long numerator = 30, long denominator = 1)
    {
        Assert.True(FrameRate.TryCreate(numerator, denominator, out var fps, out _));
        return fps;
    }
    private static VideoFormat Video(string id, int? height = 720, int? width = 1280, FrameRate? fps = null,
        string? codec = "avc1.640028", StreamPresence audio = StreamPresence.Present,
        decimal? bitrate = 1000, FormatTransport transport = FormatTransport.Https) =>
        new(id, width, height, fps ?? Rate(), codec, audio == StreamPresence.Present ? "aac" : null,
            StreamPresence.Present, audio, bitrate, null, transport);
    private static VideoFormat Audio(string id, string? codec = "aac", decimal? bitrate = 128) =>
        new(id, null, null, null, null, codec, StreamPresence.Absent, StreamPresence.Present, null, bitrate, FormatTransport.Https);
    private static VideoInfo Info(params VideoFormat[] formats)
    {
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        return new(source!, null, null, null, null, null, VideoState.Unknown, formats, []);
    }
    private static QualityOption Resolution(int height)
    {
        Assert.True(QualityOption.TryCreateResolution(height, out var quality, out _));
        return quality!;
    }
    private static FormatSelection Select(VideoInfo info, QualityOption? quality = null)
    {
        var result = new FormatSelector().Select(info, quality ?? QualityOption.Best);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        return Assert.IsType<FormatSelection>(result.Data);
    }
    [Fact]
    public void HeightPrecedesCodecTransportAndAudioConvenience()
    {
        var high = Video("high", 1080, 1920, codec: "av01.0.08M.08", audio: StreamPresence.Absent, transport: FormatTransport.Hls);
        var selection = Select(Info(Video("low"), high, Audio("a")));
        Assert.Equal("high", selection.VideoFormat.Id);
        Assert.Equal("a", selection.AudioFormat!.Id);
        Assert.Contains(FormatSelectionWarning.DeclaredQualityOnly, selection.Warnings);
    }
    [Fact]
    public void FrameRateIsComparedExactlyBeforeWidthAndCodec()
    {
        var fast = Video("fast", width: 1000, fps: Rate(30000, 1001), codec: "av01.0.08M.08");
        var decimalRate = Video("decimal", fps: Rate(2997, 100));
        Assert.Equal("fast", Select(Info(decimalRate, fast)).VideoFormat.Id);
    }
    [Fact]
    public void FrameRateComparisonDoesNotOverflowInt64()
    {
        var fast = Video("fast", fps: Rate(long.MaxValue, 2));
        var slower = Video("slower", fps: Rate(long.MaxValue, 3));
        Assert.Equal("fast", Select(Info(slower, fast)).VideoFormat.Id);
    }
    [Fact]
    public void WidthIsComparedAfterHeightAndFps()
    {
        Assert.Equal("wide", Select(Info(Video("narrow", width: 1000), Video("wide", width: 1280, codec: "vp9"))).VideoFormat.Id);
    }
    [Theory]
    [InlineData("avc1.640028", "vp9")]
    [InlineData("h264", "av01.0.08M.08")]
    [InlineData("vp09.00.40.08", "av01.0.08M.08")]
    [InlineData("av01.0.08M.08", "hevc")]
    public void CodecPreferenceIsOnlyATieCriterion(string preferred, string other) =>
        Assert.Equal("preferred", Select(Info(Video("other", codec: other), Video("preferred", codec: preferred))).VideoFormat.Id);
    [Fact]
    public void MisleadingCodecPrefixIsNotTreatedAsH264() => Assert.Equal("vp9", Select(Info(Video("fake", codec: "avc123"), Video("vp9", codec: "vp9"))).VideoFormat.Id);
    [Fact]
    public void DirectTransportPrecedesEstimatedBitrateAtAnOtherwiseMatchingQuality()
    {
        Assert.Equal("direct", Select(Info(Video("hls", bitrate: 3000, transport: FormatTransport.Hls), Video("direct", bitrate: 1900))).VideoFormat.Id);
    }
    [Fact]
    public void SameKnownFamilyThenUsesEstimatedBitrate() =>
        Assert.Equal("higher", Select(Info(Video("lower", bitrate: 500), Video("higher", bitrate: 1000))).VideoFormat.Id);
    [Fact]
    public void BitrateDoesNotOverrideCodecPreferenceAcrossFamilies() =>
        Assert.Equal("avc", Select(Info(Video("vp9", codec: "vp9", bitrate: 10000), Video("avc", bitrate: 100))).VideoFormat.Id);
    [Fact]
    public void CombinedAudioWinsOnlyWhenEarlierVideoCriteriaTie()
    {
        Assert.Equal("combined", Select(Info(Video("only", audio: StreamPresence.Absent), Video("combined"), Audio("a"))).VideoFormat.Id);
    }
    [Fact]
    public void OrdinalIdBreaksAnOtherwiseUnrankedTieWithAnExplicitWarning()
    {
        var a = Video("A"); var b = Video("b");
        foreach (var info in new[] { Info(a, b), Info(b, a) })
        {
            var selection = Select(info);
            Assert.Equal("A", selection.VideoFormat.Id);
            Assert.Contains(FormatSelectionWarning.StableIdTieBreak, selection.Warnings);
        }
    }
    [Fact]
    public void UnknownCodecFamiliesDoNotCreateANonTransitiveBitrateComparison()
    {
        var formats = new[] { Video("a", codec: "other-x", bitrate: 1), Video("b", codec: "other-y", bitrate: 10), Video("c", codec: "other-x", bitrate: 100) };
        foreach (var order in Permutations(formats)) Assert.Equal("a", Select(Info(order)).VideoFormat.Id);
    }
    private static IEnumerable<VideoFormat[]> Permutations(VideoFormat[] formats)
    {
        foreach (var first in formats)
            foreach (var second in formats.Where(f => f != first))
                yield return [first, second, formats.Single(f => f != first && f != second)];
    }
    [Fact]
    public void ExplicitHeightNeverUsesACloserOfferedHeight()
    {
        var result = new FormatSelector().Select(Info(Video("480", 480), Video("1080", 1080)), Resolution(720));
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        Assert.Equal(OperationErrorCategory.Input, result.Error.Category); Assert.Equal("Quality", result.Error.Field);
    }
    [Fact]
    public void ExplicitHeightKeepsOnlyTheRequestedHeight()
    {
        var selection = Select(Info(Video("720"), Video("1080", 1080)), Resolution(720));
        Assert.Equal("720", selection.VideoFormat.Id);
        Assert.Equal(FormatSelectionReason.RequestedResolution, selection.Reason);
    }
    [Fact]
    public void MissingFpsOnHigherResolutionIsNotSilentlyReplacedByLowerResolution()
    {
        var high = Video("high", 1080) with { FrameRate = null };
        var selection = Select(Info(Video("lower"), high));
        Assert.Equal("high", selection.VideoFormat.Id); Assert.Null(selection.VideoFormat.FrameRate);
        Assert.Contains(FormatSelectionWarning.FrameRateUnknown, selection.Warnings);
        Assert.Contains(FormatSelectionWarning.RankingMetadataIncomplete, selection.Warnings);
    }
    [Fact]
    public void MissingHeightDoesNotBecomeAnInventedResolution()
    {
        var unknown = Video("unknown", height: null);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, new FormatSelector().Select(Info(unknown), QualityOption.Best).Error!.Code);
        var selection = Select(Info(unknown, Video("known")));
        Assert.Equal("known", selection.VideoFormat.Id);
        Assert.Contains(FormatSelectionWarning.RankingMetadataIncomplete, selection.Warnings);
    }
    [Fact]
    public void UnknownVideoStreamKeepsItsWarningAndDoesNotBecomeConfirmed()
    {
        var unknown = Video("unknown", 1080) with { VideoStream = StreamPresence.Unknown };
        var selection = Select(Info(Video("lower"), unknown));
        Assert.Equal(StreamPresence.Unknown, selection.VideoFormat.VideoStream);
        Assert.Contains(FormatSelectionWarning.VideoStreamUnknown, selection.Warnings);
    }
    [Fact]
    public void AacPreferenceThenBitrateChoosesTheAudioProposal()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Absent), Audio("opus", "opus", 200), Audio("low", bitrate: 48), Audio("high", bitrate: 129)));
        Assert.Equal("high", selection.AudioFormat!.Id);
    }
    [Fact]
    public void AudioTieIsStableAndWarned()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Absent), Audio("b"), Audio("a")));
        Assert.Equal("a", selection.AudioFormat!.Id);
        Assert.Contains(FormatSelectionWarning.StableIdTieBreak, selection.Warnings);
    }
    [Fact]
    public void AudioBitrateCannotOverrideDifferentKnownCodecFamilies()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Absent), Audio("aac", bitrate: 48), Audio("opus", "opus", 1000)));
        Assert.Equal("aac", selection.AudioFormat!.Id);
    }
    [Fact]
    public void UnknownEmbeddedAudioDoesNotAcquireAdditionalAudio()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Unknown), Audio("a")));
        Assert.Null(selection.AudioFormat);
        Assert.Contains(FormatSelectionWarning.AudioStreamUnknown, selection.Warnings);
    }
    [Fact]
    public void UnknownAudioOnlyStreamIsNotAssumedPresentOrSilent()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Absent), Audio("a") with { AudioStream = StreamPresence.Unknown }));
        Assert.Null(selection.AudioFormat);
        Assert.Contains(FormatSelectionWarning.AudioSourceRequiresVerification, selection.Warnings);
    }
    [Fact]
    public void CombinedAudioIsNotReplacedWithHigherRankedSeparateAudio()
    {
        Assert.Null(Select(Info(Video("v"), Audio("a", bitrate: 1000))).AudioFormat);
    }
    [Fact]
    public void EmptyOrAudioOnlyMetadataHasNoVideoCandidate()
    {
        foreach (var info in new[] { Info(), Info(Audio("a")) })
            Assert.Equal(OperationErrorCode.UnsupportedFormat, new FormatSelector().Select(info, QualityOption.Best).Error!.Code);
    }
    [Fact]
    public void DuplicateIdsAndInvalidStreamTransportEnumsFailClosed()
    {
        var v = Video("v");
        foreach (var info in new[] { Info(v, v), Info(v with { VideoStream = (StreamPresence)999 }), Info(v with { Transport = (FormatTransport)999 }) })
            Assert.Equal(OperationErrorCode.InvalidMetadata, new FormatSelector().Select(info, QualityOption.Best).Error!.Code);
    }
    [Theory]
    [InlineData("chzzk", "15430558", "hls-8384", null)]
    [InlineData("youtube", "sFXPM_sfJNo", "137", "140")]
    public void ActualFixtureSelectionIsStableWhenTheExtractorOrderChanges(string provider, string id, string expectedVideo, string? expectedAudio)
    {
        VideoSourceReference.TryCreate(provider == "chzzk" ? VideoPlatform.Chzzk : VideoPlatform.YouTube, id, out var source);
        var result = new YtDlpMetadataParser().Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", "selection-" + provider + ".json")), source!);
        var original = Assert.IsType<VideoInfo>(result.Data);
        foreach (var formats in new[] { original.Formats, original.Formats.Reverse().ToArray() })
        {
            var reordered = new VideoInfo(original.Source, null, original.Duration, null, null, null, original.State, formats, original.Issues);
            var selection = Select(reordered);
            Assert.Equal(expectedVideo, selection.VideoFormat.Id); Assert.Equal(expectedAudio, selection.AudioFormat?.Id);
            Assert.Equal(original.Formats.Single(f => f.Id == expectedVideo).FrameRate, selection.VideoFormat.FrameRate);
        }
    }
    [Fact]
    public void RankingCriteriaExposeThePolicyWithoutClaimingMeasuredQuality()
    {
        var selection = Select(Info(Video("v", audio: StreamPresence.Absent), Audio("a")));
        Assert.Equal(FormatSelectionCriterion.Height, selection.RankingCriteria[0]);
        Assert.Contains(FormatSelectionCriterion.AudioBitrateEstimate, selection.RankingCriteria);
        Assert.Throws<NotSupportedException>(() => ((IList<FormatSelectionCriterion>)selection.RankingCriteria).Clear());
        Assert.Contains(FormatSelectionWarning.DeclaredQualityOnly, selection.Warnings);
    }
    [Fact]
    public void MissingBitrateOfAnUnselectedCandidateStillKeepsTheComparisonUncertainty()
    {
        var selection = Select(Info(Video("known"), Video("unknown", bitrate: null)));
        Assert.Equal("known", selection.VideoFormat.Id);
        Assert.Contains(FormatSelectionWarning.RankingMetadataIncomplete, selection.Warnings);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidBitrateInACallerConstructedSnapshotFailsClosed(int invalid)
    {
        var video = Video("v") with { VideoBitrateKbps = invalid };
        Assert.Equal(OperationErrorCode.InvalidMetadata, new FormatSelector().Select(Info(video), QualityOption.Best).Error!.Code);
    }
}
