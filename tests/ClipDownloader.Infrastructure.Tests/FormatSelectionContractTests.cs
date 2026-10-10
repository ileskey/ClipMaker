using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class FormatSelectionContractTests
{
    private static VideoFormat Video(string id = "v", int? height = 720, StreamPresence audio = StreamPresence.Present) =>
        new(id, 1280, height, Rate(60), "avc1", audio == StreamPresence.Present ? "aac" : null, StreamPresence.Present, audio);
    private static VideoFormat Audio(string id = "a") => new(id, null, null, null, null, "aac", StreamPresence.Absent, StreamPresence.Present);
    private static FrameRate Rate(int value)
    {
        Assert.True(FrameRate.TryCreate(value, 1, out var rate, out _));
        return rate;
    }
    private static VideoInfo Info(params VideoFormat[] formats)
    {
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        return new(source!, null, null, null, null, null, VideoState.Unknown, formats, []);
    }
    private static QualityOption Resolution(int height)
    {
        Assert.True(QualityOption.TryCreateResolution(height, out var quality, out var error));
        Assert.Null(error);
        return quality!;
    }
    private static FormatSelection Selection(OperationResult<FormatSelection> result)
    {
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        return Assert.IsType<FormatSelection>(result.Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ResolutionRejectsNonPositiveHeight(int height)
    {
        Assert.False(QualityOption.TryCreateResolution(height, out var quality, out var error));
        Assert.Null(quality); Assert.Equal(OperationErrorCode.InvalidInput, error!.Code);
        Assert.Equal(OperationErrorCategory.Input, error.Category); Assert.Equal("Quality", error.Field);
    }
    [Theory]
    [InlineData(144)]
    [InlineData(540)]
    [InlineData(720)]
    [InlineData(int.MaxValue)]
    public void ResolutionIsARequestNotProofThatItIsOffered(int height)
    {
        var quality = Resolution(height);
        Assert.Equal(QualityKind.Resolution, quality.Kind); Assert.Equal(height, quality.Height);
        Assert.Equal(quality, Resolution(height));
        Assert.NotEqual(quality, QualityOption.Best);
    }
    [Fact]
    public void BestHasNoInventedHeight()
    {
        Assert.Equal(QualityKind.Best, QualityOption.Best.Kind);
        Assert.Null(QualityOption.Best.Height);
    }
    [Fact]
    public void CombinedFormatKeepsTheActualSelectedFpsAndHasNoSeparateAudio()
    {
        var video = Video();
        var selection = Selection(FormatSelection.Create(Info(video), Resolution(720), video, null, FormatSelectionReason.RequestedResolution));
        Assert.Same(video, selection.VideoFormat); Assert.Null(selection.AudioFormat);
        Assert.Equal(60, selection.VideoFormat.FrameRate!.Numerator); Assert.Empty(selection.Warnings);
        Assert.Equal(FormatSelectionReason.RequestedResolution, selection.Reason);
    }
    [Fact]
    public void SeparateAudioDoesNotReplaceVideoFps()
    {
        var video = Video(audio: StreamPresence.Absent);
        var audio = Audio() with { FrameRate = Rate(30) };
        var selection = Selection(FormatSelection.Create(Info(video, audio), Resolution(720), video, audio, FormatSelectionReason.RequestedResolution));
        Assert.Same(audio, selection.AudioFormat);
        Assert.Equal(60, selection.VideoFormat.FrameRate!.Numerator);
        Assert.Empty(selection.Warnings);
    }
    [Theory]
    [InlineData(1080)]
    [InlineData(480)]
    [InlineData(null)]
    public void ExplicitResolutionNeverFallsBack(int? offered)
    {
        var video = Video(height: offered);
        var result = FormatSelection.Create(Info(video), Resolution(720), video, null, FormatSelectionReason.RequestedResolution);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        Assert.Equal(OperationErrorCategory.Input, result.Error.Category); Assert.Null(result.Data);
    }
    [Fact]
    public void UnknownValuesRemainWarningsRatherThanDefaults()
    {
        var video = new VideoFormat("unknown", null, null, null, null, null, StreamPresence.Unknown, StreamPresence.Unknown);
        var selection = Selection(FormatSelection.Create(Info(video), QualityOption.Best, video, null, FormatSelectionReason.RequestedBest));
        Assert.Null(selection.VideoFormat.FrameRate); Assert.Null(selection.AudioFormat);
        Assert.Equal(StreamPresence.Unknown, selection.VideoFormat.AudioStream);
        Assert.Equal(6, selection.Warnings.Count);
        Assert.Contains(FormatSelectionWarning.VideoStreamUnknown, selection.Warnings);
        Assert.Contains(FormatSelectionWarning.AudioStreamUnknown, selection.Warnings);
        Assert.DoesNotContain(FormatSelectionWarning.AudioSourceRequiresVerification, selection.Warnings);
    }
    [Fact]
    public void VideoOnlyWithoutAudioDoesNotProveThatTheOriginalIsSilent()
    {
        var video = Video(audio: StreamPresence.Absent);
        var selection = Selection(FormatSelection.Create(Info(video), QualityOption.Best, video, null, FormatSelectionReason.RequestedBest));
        Assert.Contains(FormatSelectionWarning.AudioSourceRequiresVerification, selection.Warnings);
        Assert.Null(selection.AudioFormat);
    }
    [Theory]
    [InlineData(StreamPresence.Present)]
    [InlineData(StreamPresence.Unknown)]
    public void EmbeddedAudioOrUnknownPresenceCannotBeSilentlyReplaced(StreamPresence presence)
    {
        var video = Video(audio: presence); var audio = Audio();
        Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(Info(video, audio), QualityOption.Best,
            video, audio, FormatSelectionReason.RequestedBest).Error!.Code);
    }
    [Theory]
    [InlineData(StreamPresence.Present, StreamPresence.Present)]
    [InlineData(StreamPresence.Unknown, StreamPresence.Present)]
    [InlineData(StreamPresence.Absent, StreamPresence.Unknown)]
    [InlineData(StreamPresence.Absent, StreamPresence.Absent)]
    public void SeparateAudioMustBeConfirmedAudioOnly(StreamPresence videoPresence, StreamPresence audioPresence)
    {
        var video = Video(audio: StreamPresence.Absent);
        var audio = Audio() with { VideoStream = videoPresence, AudioStream = audioPresence };
        Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(Info(video, audio), QualityOption.Best,
            video, audio, FormatSelectionReason.RequestedBest).Error!.Code);
    }
    [Fact]
    public void AudioOnlyFormatCannotBeTheVideoSelection()
    {
        var audio = Audio();
        Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(Info(audio), QualityOption.Best,
            audio, null, FormatSelectionReason.RequestedBest).Error!.Code);
    }
    [Fact]
    public void FormatMustMatchTheSnapshotAndHaveAUniqueId()
    {
        var video = Video();
        foreach (var info in new[] { Info(), Info(video, video), Info(video with { Height = 1080 }) })
            Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(info, QualityOption.Best,
                video, null, FormatSelectionReason.RequestedBest).Error!.Code);
    }
    [Fact]
    public void SeparateAudioMustAlsoBelongToTheSnapshot()
    {
        var video = Video(audio: StreamPresence.Absent);
        Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(Info(video), QualityOption.Best,
            video, Audio(), FormatSelectionReason.RequestedBest).Error!.Code);
    }
    [Fact]
    public void WarningCollectionIsImmutable()
    {
        var video = Video(audio: StreamPresence.Unknown);
        var selection = Selection(FormatSelection.Create(Info(video), QualityOption.Best, video, null, FormatSelectionReason.RequestedBest));
        Assert.Throws<NotSupportedException>(() => ((IList<FormatSelectionWarning>)selection.Warnings).Clear());
    }
    [Fact]
    public void ReasonMustMatchRequestAndInvalidEnumsAreNotAccepted()
    {
        var video = Video(); var info = Info(video);
        Assert.Throws<ArgumentException>(() => FormatSelection.Create(info, QualityOption.Best, video, null, FormatSelectionReason.RequestedResolution));
        Assert.Throws<ArgumentException>(() => FormatSelection.Create(info, Resolution(720), video, null, FormatSelectionReason.RequestedBest));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormatSelection.Create(info, QualityOption.Best, video, null, (FormatSelectionReason)999));
        var invalid = video with { AudioStream = (StreamPresence)999 };
        Assert.Equal(OperationErrorCode.InvalidMetadata, FormatSelection.Create(Info(invalid), QualityOption.Best, invalid, null, FormatSelectionReason.RequestedBest).Error!.Code);
    }
}
