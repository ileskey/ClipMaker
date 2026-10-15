using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Sources;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SupportedVideoSourceParserTests
{
    private readonly SupportedVideoSourceParser parser = new();
    private const string Id = "sFXPM_sfJNo";
    private const string YouTubePage = "https://www.youtube.com/watch?v=" + Id;

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://m.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://youtu.be/sFXPM_sfJNo")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com:443/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com:0443/watch?v=sFXPM_sfJNo")]
    [InlineData(" \t https://youtu.be/sFXPM_sfJNo \r\n")]
    [InlineData("https://www.youtube.com/watch?utm=tracking&v=sFXPM_sfJNo&list=PL123&t=123#time=999")]
    [InlineData("https://youtu.be/sFXPM_sfJNo?si=tracking&t=123#fragment")]
    [InlineData("https://www.youtube.com/watch?%76=%73FXPM%5FsfJNo")]
    [InlineData("https://youtu.be/%73FXPM%5FsfJNo")]
    [InlineData("https://www.youtube.com/watch?&&v=sFXPM_sfJNo&&")]
    public void OfficialYouTubeAliasesProduceSameCanonicalIdentity(string text)
    {
        var result = parser.Parse(text);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.NotNull(result.Data);
        Assert.Equal(VideoPlatform.YouTube, result.Data.Platform);
        Assert.Equal(Id, result.Data.Id);
        Assert.Equal(YouTubePage, result.Data.PageUrl.AbsoluteUri);
        Assert.Equal(result.Data, parser.Parse(result.Data.PageUrl).Data);
    }

    [Theory]
    [InlineData("https://chzzk.naver.com/video/15430558", "15430558")]
    [InlineData("https://CHZZK.NAVER.COM:443/video/15430558?tracking=1#start=99", "15430558")]
    [InlineData("https://chzzk.naver.com/video/%31%35%34%33%30%35%35%38", "15430558")]
    [InlineData("https://chzzk.naver.com/video/0001", "0001")]
    [InlineData("https://chzzk.naver.com/video/0", "0")]
    [InlineData("https://chzzk.naver.com/video/9999999999999999999999999999999999999999", "9999999999999999999999999999999999999999")]
    public void ChzzkIdsAreAsciiStringsWithoutNumericOverflowOrExistenceClaims(string text, string id)
    {
        var result = parser.Parse(text);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal(VideoPlatform.Chzzk, result.Data.Platform);
        Assert.Equal(id, result.Data.Id);
        Assert.Equal($"https://chzzk.naver.com/video/{id}", result.Data.PageUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=AbCdEfGhI-_", "AbCdEfGhI-_")]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", "abcdefghijk")]
    [InlineData("https://www.youtube.com/watch?v=ABCDEFGHIJK", "ABCDEFGHIJK")]
    [InlineData("https://www.youtube.com/watch?v=12345678901", "12345678901")]
    public void YouTubeIdCaseAndAllowedAlphabetArePreserved(string text, string id)
    {
        var result = parser.Parse(text);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(id, result.Data!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a URL")]
    [InlineData("//www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https:/www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_ sfJNo")]
    [InlineData("https://www.youtube.com/\nwatch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo\0")]
    [InlineData("https://www.youtube.com\\watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo&x=%")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo&x=%0")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo&x=%GG")]
    [InlineData("https://www.youtube.com:/watch?v=sFXPM_sfJNo")]
    public void MalformedInputIsInputErrorAndDoesNotReturnSource(string? text)
    {
        var result = parser.Parse(text);
        AssertFailed(result, OperationErrorCode.InvalidInput);
    }

    [Theory]
    [InlineData("http://www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("ftp://www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com:444/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com:80/watch?v=sFXPM_sfJNo")]
    [InlineData("https://evil.example/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com.evil.example/watch?v=sFXPM_sfJNo")]
    [InlineData("https://evil.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://youtube-nocookie.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtu.be/sFXPM_sfJNo")]
    [InlineData("https://www.chzzk.naver.com/video/15430558")]
    [InlineData("https://www.youtube.com./watch?v=sFXPM_sfJNo")]
    [InlineData("https://yоutube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://127.0.0.1/watch?v=sFXPM_sfJNo")]
    [InlineData("https://[::1]/watch?v=sFXPM_sfJNo")]
    [InlineData("https://[::1]:443/watch?v=sFXPM_sfJNo")]
    public void UnsupportedSchemePortAndHostDoNotUseHeuristicSuffixMatching(string text) =>
        AssertFailed(parser.Parse(text), OperationErrorCode.UnsupportedSource);

    [Theory]
    [InlineData("https://www.youtube.com/shorts/sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/embed/sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/live/sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://www.youtube.com/WATCH?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch/?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/foo/../watch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/%77atch?v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/%2e/watch?v=sFXPM_sfJNo")]
    [InlineData("https://chzzk.naver.com/live/15430558")]
    [InlineData("https://chzzk.naver.com/foo/../video/15430558")]
    [InlineData("https://chzzk.naver.com/%76ideo/15430558")]
    public void UnsupportedRawPathsAreNotPromotedByUriNormalization(string text)
    {
        AssertFailed(parser.Parse(text), OperationErrorCode.UnsupportedSource);
        AssertFailed(parser.Parse(new Uri(text)), OperationErrorCode.UnsupportedSource);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    [InlineData("https://www.youtube.com/watch#v=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?V=sFXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?v")]
    [InlineData("https://www.youtube.com/watch?v=")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM_sfJNo0")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM+sfJNo")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM%20sfJN")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM%2FsfJNo")]
    [InlineData("https://www.youtube.com/watch?v=%2573FXPM_sfJNo")]
    [InlineData("https://www.youtube.com/watch?v=%FF1234567890")]
    [InlineData("https://www.youtube.com/watch?v=sFXPM%00sfJNo")]
    [InlineData("https://youtu.be/")]
    [InlineData("https://youtu.be/sFXPM_sfJNo/extra")]
    [InlineData("https://youtu.be/sFXPM_sfJNo/")]
    [InlineData("https://chzzk.naver.com/video/")]
    [InlineData("https://chzzk.naver.com/video/-1")]
    [InlineData("https://chzzk.naver.com/video/1.0")]
    [InlineData("https://chzzk.naver.com/video/１２３")]
    [InlineData("https://chzzk.naver.com/video/%252F")]
    [InlineData("https://chzzk.naver.com/video/1/../15430558")]
    public void MissingInvalidAndMultiplyEscapedIdsAreNotSilentlyRepaired(string text) =>
        AssertFailed(parser.Parse(text), OperationErrorCode.InvalidInput);

    [Theory]
    [InlineData("v=sFXPM_sfJNo&v=sFXPM_sfJNo")]
    [InlineData("v=sFXPM_sfJNo&v=AbCdEfGhI-_")]
    [InlineData("v=&v=sFXPM_sfJNo")]
    [InlineData("v=sFXPM_sfJNo&%76=AbCdEfGhI-_")]
    [InlineData("%76=sFXPM_sfJNo&v=AbCdEfGhI-_")]
    [InlineData("v=sFXPM_sfJNo&v")]
    public void DuplicateVideoParametersAreRejectedEvenWhenIdenticalOrEncoded(string query) =>
        AssertFailed(parser.Parse("https://www.youtube.com/watch?" + query), OperationErrorCode.InvalidInput);

    [Theory]
    [InlineData("https://user:PRIVATE_SECRET@www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://user@www.youtube.com/watch?v=sFXPM_sfJNo")]
    [InlineData("https://@www.youtube.com/watch?v=sFXPM_sfJNo")]
    public void UserInfoIncludingEmptyUsernameIsRejectedWithoutEchoingSensitiveUrl(string text)
    {
        var result = parser.Parse(text);
        AssertFailed(result, OperationErrorCode.InvalidInput);
        Assert.DoesNotContain("PRIVATE_SECRET", result.Error!.Message);
        Assert.DoesNotContain("https://", result.Error.Message);
    }

    [Fact]
    public void UriEntryPointPreservesOriginalStructureAndDoesNotAcceptRelativeUris()
    {
        var canonical = new Uri(YouTubePage);
        Assert.Equal(parser.Parse(YouTubePage).Data, parser.Parse(canonical).Data);
        AssertFailed(parser.Parse(new Uri("watch?v=" + Id, UriKind.Relative)), OperationErrorCode.InvalidInput);
        Assert.Throws<ArgumentNullException>(() => parser.Parse((Uri)null!));
    }

    private static void AssertFailed(OperationResult<VideoSourceReference> result, OperationErrorCode code)
    {
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(OperationErrorCategory.Input, result.Error.Category);
        Assert.Equal("Url", result.Error.Field);
    }
}
