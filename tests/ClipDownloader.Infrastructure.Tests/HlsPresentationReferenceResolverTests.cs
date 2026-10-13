using System.Net;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsPresentationReferenceResolverTests
{
    private const string Master = "#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"234\",URI=\"a/itag/234/index.m3u8\"\n#EXT-X-STREAM-INF:AUDIO=\"234\"\nv/itag/270/index.m3u8\n";
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(Master)) });
        }
    }
    private static SelectedSourceLocators Source(string fault)
    {
        VideoSourceReference.TryCreate(VideoPlatform.YouTube, "sFXPM_sfJNo", out var source);
        var video = new VideoFormat("137", 1920, 1080, F.Rate(30), "h264", null, StreamPresence.Present, StreamPresence.Absent, Transport: FormatTransport.Https);
        var audio = new VideoFormat("140", null, null, null, null, "aac", StreamPresence.Absent, StreamPresence.Present, Transport: FormatTransport.Https);
        var info = new VideoInfo(source!, null, TimeSpan.FromSeconds(120), null, null, null, VideoState.Completed, [video, audio], []);
        var choice = FormatSelection.Create(info, QualityOption.Best, video, audio, FormatSelectionReason.RequestedBest).Data!;
        var snapshot = SelectedSourceSnapshot.Create(Guid.NewGuid(), info, choice, ClipMode.Fast).Data!;
        var headers = new Dictionary<string, string>();
        var master = new Uri("https://media.example/master.m3u8");
        var vr = new SelectedMediaLocator(video with { Id = "270", Transport = FormatTransport.Hls }, new("https://media.example/old/itag/270/x"), master, headers);
        var ar = new SelectedMediaLocator(audio with { Id = "234", Transport = FormatTransport.Hls, AudioStream = fault == "unknown" ? StreamPresence.Unknown : StreamPresence.Present }, new("https://media.example/old/itag/234/x"),
            fault == "foreign" ? new("https://media.example/other.m3u8") : master, headers);
        return new(snapshot, info, new(video, new("https://media.example/video.mp4"), null, headers),
            new(audio, new("https://media.example/audio.mp4"), null, headers), fault == "missing" ? [vr] : fault == "duplicate" ? [vr, vr, ar] : [vr, ar]);
    }
    [Theory]
    [InlineData("success")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("budget")]
    [InlineData("cancel")]
    public async Task SupplementalMasterReadIsBoundedAndDoesNotChangeOutputChoice(string fault)
    {
        var source = Source(fault);
        var handler = new Handler(); using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsPresentationReferenceResolver(reader).ResolveAsync(source,
            new(fault == "budget" ? 100 : 1000, F.Time(12)), 100,
            fault == "cancel" ? new(true) : TestContext.Current.CancellationToken);
        Assert.Equal("137", source.Selection.Selection.VideoFormat.Id);
        Assert.Equal("140", source.Selection.Selection.AudioFormat!.Id);
        if (fault is "success" or "unknown")
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(100 + Encoding.UTF8.GetByteCount(Master), result.Data!.BudgetUsed);
            Assert.Equal("https://media.example/v/itag/270/index.m3u8", result.Data.Video.Uri.AbsoluteUri);
            Assert.Equal("https://media.example/a/itag/234/index.m3u8", result.Data.Audio.Uri.AbsoluteUri);
            Assert.Equal(1, handler.Calls);
        }
        else
        {
            Assert.Null(result.Data);
            Assert.Equal(fault == "cancel" ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
            Assert.Equal(0, handler.Calls);
        }
    }
}
