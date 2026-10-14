using System.Net;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsReferenceTests
{
    private static readonly Uri leaf = new("https://media.example/path/selected.m3u8?token=private-test-value");
    private const string playlist = "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-DISCONTINUITY-SEQUENCE:0\n#EXT-X-MAP:URI=\"init,part.mp4\"\n#EXT-X-PROGRAM-DATE-TIME:2026-10-13T00:00:00Z\n#EXTINF:1.999333,\na.m4s\n#EXTINF:2.000667,\nb.m4s\n#EXTINF:1.5,\nc.m4s\n#EXT-X-ENDLIST\n";
    private static SignedMediaTime Time(long n, long d = 1)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _)); return time;
    }
    private static PresentationRange Range(SignedMediaTime start, SignedMediaTime end)
    {
        Assert.True(PresentationRange.TryCreate(start, end, out var range, out _)); return range;
    }
    private static OperationResult<HlsMediaPlaylist> Parse(string text) => new HlsMediaPlaylistParser().Parse(Encoding.UTF8.GetBytes(text), leaf);

    [Fact]
    public void RetainsExactExtinfCoordinatesSequenceAndRelativeUriResolution()
    {
        var result = Parse(playlist);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var data = result.Data!;
        Assert.Equal(Time(11, 2), data.Duration);
        Assert.Equal(new long[] { 1, 2, 3 }, data.Segments.Select(segment => segment.Sequence));
        Assert.Equal(Time(1999333, 1000000), data.Segments[0].PlaylistRange.End);
        Assert.Equal(Time(4), data.Segments[1].PlaylistRange.End);
        Assert.Equal("https://media.example/path/init,part.mp4", data.Initialization.AbsoluteUri);
        Assert.Equal("https://media.example/path/a.m4s", data.Segments[0].Uri.AbsoluteUri);
        Assert.Equal("", data.Segments[0].Uri.Query); // Never copy the parent's signature to a child.
        Assert.DoesNotContain("private-test-value", data.ToString()!);
    }

    [Fact]
    public void SelectsIntersectingWholeReferencesWithExclusiveOutAndRealFetchBudget()
    {
        var data = Parse(playlist).Data!;
        var planner = new HlsReferencePlanner();
        var first = planner.Plan(data, Range(Time(0), Time(1999333, 1000000)), Time(2), 1);
        Assert.Equal(OperationStatus.Succeeded, first.Status);
        Assert.Equal(data.Id, first.Data!.PlaylistId);
        Assert.Single(first.Data.Segments);
        var crosses = Range(Time(1999332, 1000000), Time(2));
        var both = planner.Plan(data, crosses, Time(4), 2);
        Assert.Equal(2, both.Data!.Segments.Count);
        var spanError = planner.Plan(data, crosses, Time(3), 2).Error!;
        Assert.Equal(OperationErrorCode.UnsupportedFormat, spanError.Code);
        Assert.Equal("HlsFetchedSpanBudget", spanError.Field);
        Assert.Contains("4초", spanError.Message);
        Assert.Contains("상한 3초", spanError.Message);
        var countError = planner.Plan(data, crosses, Time(4), 1).Error!;
        Assert.Equal(OperationErrorCode.UnsupportedFormat, countError.Code);
        Assert.Equal("HlsSegmentCountBudget", countError.Field);
        Assert.Contains("2개", countError.Message);
        Assert.Contains("상한 1개", countError.Message);
        foreach (var invalid in new[] { Range(Time(-1), Time(1)), Range(Time(4), Time(6)) })
        {
            var error = planner.Plan(data, invalid, Time(4), 2).Error!;
            Assert.Equal(OperationErrorCode.InvalidRange, error.Code);
            Assert.Equal("HlsPlaylistRange", error.Field);
        }
        Assert.DoesNotContain("private-test-value", spanError.Message);
    }

    [Fact]
    public void ReportsExactFractionalWholeSpanRatherThanRequestedDuration()
    {
        var data = Parse(playlist).Data!;
        var planner = new HlsReferencePlanner();
        var requested = Range(Time(1), Time(3, 2));
        var failed = planner.Plan(data, requested, Time(3, 2), 1);
        Assert.Equal("HlsFetchedSpanBudget", failed.Error!.Field);
        Assert.Contains("1999333/1000000초", failed.Error.Message);
        Assert.Contains("상한 3/2초", failed.Error.Message);
        Assert.Equal(OperationStatus.Succeeded, planner.Plan(data, requested, Time(1999333, 1000000), 1).Status);
    }

    [Fact]
    public void ThirtySecondClipWithFastMarginsExceedsThirtySecondWholeReferenceBudget()
    {
        var segments = Enumerable.Range(0, 12).Select(i => new HlsSegment(i,
            new Uri(leaf, $"{i}.m4s"), Range(Time(i * 6), Time((i + 1) * 6)))).ToArray();
        var data = new HlsMediaPlaylist(leaf, new Uri(leaf, "init.mp4"), segments, Time(72), 100);
        // Requested [18,48), with one initial-reference span on either side.
        var fetchWindow = Range(Time(12), Time(54));
        var planner = new HlsReferencePlanner();
        var failed = planner.Plan(data, fetchWindow, Time(30), 64);
        Assert.Equal("HlsFetchedSpanBudget", failed.Error!.Field);
        Assert.Contains("42초", failed.Error.Message);
        Assert.Contains("상한 30초", failed.Error.Message);
        var sufficient = planner.Plan(data, fetchWindow, Time(42), 64);
        Assert.Equal(7, sufficient.Data!.Segments.Count);
    }

    [Theory]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,URI=\"key\"")]
    [InlineData("#EXT-X-BYTERANGE:100@0")]
    [InlineData("#EXT-X-DISCONTINUITY")]
    [InlineData("#EXT-X-GAP")]
    [InlineData("#EXT-X-STREAM-INF:BANDWIDTH=1000")]
    [InlineData("#EXT-X-PART:DURATION=0.5,URI=\"part\"")]
    public void ExplicitlyRejectsUnverifiedHlsModesRatherThanSkippingTheirTags(string tag)
    {
        var result = Parse(playlist.Replace("#EXTINF:1.999333,", tag + "\n#EXTINF:1.999333,", StringComparison.Ordinal));
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        Assert.DoesNotContain("private-test-value", result.Error.Message);
    }

    [Theory]
    [InlineData("#EXT-X-ENDLIST\n", "")]
    [InlineData("#EXT-X-DISCONTINUITY-SEQUENCE:0", "#EXT-X-DISCONTINUITY-SEQUENCE:1")]
    [InlineData("#EXT-X-MAP:URI=\"init,part.mp4\"", "#EXT-X-MAP:URI=\"init,part.mp4\",BYTERANGE=\"100@0\"")]
    [InlineData("#EXT-X-VERSION:7", "#EXT-X-VERSION:8")]
    public void RejectsNonFinalOrUnsupportedInitializationAndVersion(string from, string to) =>
        Assert.Equal(OperationStatus.Failed, Parse(playlist.Replace(from, to, StringComparison.Ordinal)).Status);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("1e0")]
    [InlineData("2.5")]
    public void RejectsInvalidOrTargetDurationViolatingExtinf(string duration) =>
        Assert.Equal(OperationStatus.Failed, Parse(playlist.Replace("#EXTINF:1.999333,", "#EXTINF:" + duration + ",", StringComparison.Ordinal)).Status);

    [Fact]
    public void DetectsDanglingDurationsDuplicateMetadataUnsafeUriAndIncompleteInput()
    {
        foreach (string changed in new[] {
            playlist.Replace("a.m4s\n", "", StringComparison.Ordinal),
            playlist.Replace("#EXT-X-MEDIA-SEQUENCE:1", "#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-MEDIA-SEQUENCE:1", StringComparison.Ordinal),
            playlist.Replace("a.m4s", "http://media.example/a.m4s", StringComparison.Ordinal),
            playlist.Replace("a.m4s", "https://user:pass@media.example/a.m4s", StringComparison.Ordinal),
            playlist.Replace("#EXT-X-ENDLIST", "#EXT-X-ENDLIST\nextra.m4s", StringComparison.Ordinal) })
            Assert.Equal(OperationStatus.Failed, Parse(changed).Status);
        Assert.Equal(OperationStatus.Failed, new HlsMediaPlaylistParser().Parse(new byte[] { 0xff }, leaf).Status);
        Assert.Equal(OperationStatus.Failed, new HlsMediaPlaylistParser().Parse(new byte[HlsMediaPlaylistParser.MaximumBytes + 1], leaf).Status);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(response(request)); }
    }
    [Fact]
    public async Task ReadsOnlySelectedLeafAndRejectsMasterWithoutVariantFallback()
    {
        var format = new VideoFormat("hls-selected", 1920, 1080, null, "h264", "aac", StreamPresence.Present, StreamPresence.Present,
            Transport: FormatTransport.Hls);
        var locator = new SelectedMediaLocator(format, leaf, new("https://media.example/master.m3u8"), new Dictionary<string, string>());
        var handler = new Handler(request =>
        {
            Assert.Equal(leaf, request.RequestUri);
            return new(HttpStatusCode.OK) { Content = new StringContent(playlist) };
        });
        using (var transport = new BoundedSourceHttpReader(handler))
        {
            var result = await new HlsSelectedPlaylistReader(transport).ReadAsync(locator, 4096, TestContext.Current.CancellationToken);
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(1, handler.Calls);
        }
        var masterHandler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nother.m3u8\n") });
        using (var transport = new BoundedSourceHttpReader(masterHandler))
        {
            Assert.Equal(OperationErrorCode.UnsupportedFormat, (await new HlsSelectedPlaylistReader(transport).ReadAsync(locator, 4096,
                TestContext.Current.CancellationToken)).Error!.Code);
            Assert.Equal(1, masterHandler.Calls);
        }
    }
}
