using ClipDownloader.Application.Models;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class VideoSourceReferenceTests
{
    [Theory]
    [InlineData(VideoPlatform.YouTube, "AbCdEfGhI-_", "https://www.youtube.com/watch?v=AbCdEfGhI-_")]
    [InlineData(VideoPlatform.Chzzk, "0001", "https://chzzk.naver.com/video/0001")]
    public void FactoryOwnsCanonicalPageUrlAndValueEquality(VideoPlatform platform, string id, string page)
    {
        Assert.True(VideoSourceReference.TryCreate(platform, id, out var source));
        Assert.True(VideoSourceReference.TryCreate(platform, id, out var equivalent));
        Assert.Equal(source, equivalent);
        Assert.Equal(source.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(page, source.PageUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData(VideoPlatform.YouTube, null)]
    [InlineData(VideoPlatform.YouTube, "")]
    [InlineData(VideoPlatform.YouTube, "short")]
    [InlineData(VideoPlatform.YouTube, "abcdefghij.")]
    [InlineData(VideoPlatform.Chzzk, "")]
    [InlineData(VideoPlatform.Chzzk, "1/2")]
    [InlineData(VideoPlatform.Chzzk, "+1")]
    [InlineData(VideoPlatform.Chzzk, "１")]
    public void InvalidProviderIdsCannotCreateReferences(VideoPlatform platform, string? id)
    {
        Assert.False(VideoSourceReference.TryCreate(platform, id, out var source));
        Assert.Null(source);
    }

    [Fact]
    public void InvalidPlatformIsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoSourceReference.TryCreate((VideoPlatform)99, "1", out _));

    [Fact]
    public void LongAsciiIdDoesNotAcquireUnspecifiedNumericOrLegacyUriLimits()
    {
        string id = new('1', 100000);
        Assert.True(VideoSourceReference.TryCreate(VideoPlatform.Chzzk, id, out var source));
        Assert.Equal(id, source.Id);
        Assert.Equal("https://chzzk.naver.com/video/" + id, source.PageUrl.AbsoluteUri);
    }
}
