using ClipMaker.Media.Core.Models;
using System.Net;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsInitialReferenceInspectorTests
{
    private const string Playlist = "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:2,\na.m4s\n#EXTINF:2,\nb.m4s\n#EXT-X-ENDLIST\n";
    private sealed class Fixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(".artifacts"), "initial-inspector-tests", Guid.NewGuid().ToString("N"));
        internal SelectedSourceInspectionRequest Request { get; }
        internal SelectedSourceLocators Locators { get; }
        internal Fixture(long bytes = 4096, long span = 4, FormatTransport transport = FormatTransport.Hls)
        {
            Directory.CreateDirectory(DirectoryPath);
            VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
            var format = new VideoFormat("v", 1920, 1080, F.Rate(), "h264", "aac", StreamPresence.Present,
                StreamPresence.Present, Transport: transport);
            var info = new VideoInfo(source!, null, TimeSpan.FromSeconds(120), null, null, null, VideoState.Completed, [format], []);
            var selection = FormatSelection.Create(info, QualityOption.Best, format, null, FormatSelectionReason.RequestedBest).Data!;
            var snapshot = SelectedSourceSnapshot.Create(Guid.NewGuid(), info, selection, ClipMode.Fast).Data!;
            Request = new(Guid.NewGuid(), snapshot, SourceInspectionPurpose.SourceTimeline, DirectoryPath, new(bytes, F.Time(span)));
            Locators = new(snapshot, info, new(format, new("https://media.example/leaf.m3u8"), null, new Dictionary<string,string>()), null);
        }
        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(DirectoryPath)) File.Delete(file);
            Directory.Delete(DirectoryPath);
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Respond?.Invoke(request) ?? new(HttpStatusCode.OK) {Content = new ByteArrayContent(
                request.RequestUri.AbsolutePath.EndsWith("m3u8", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(Playlist) : [1,2,3])});
        }
    }
    private static ProbedInitialReference Observations(HlsCollectedReference reference)
    {
        var data = new InitialReferenceObservationParser().Parse(Encoding.UTF8.GetBytes("""{"streams":[{"index":0,"codec_type":"video","time_base":"1/6000"}],"frames":[],"packets":[]}""")).Data!;
        return new(reference, "test", data, new(6000, Array.AsReadOnly(new[] {new Mp4TrackTiming(1,6000,false,Array.AsReadOnly(Array.Empty<Mp4EditObservation>()))})), [], []);
    }
    [Fact]
    public async Task SameSelectionBudgetAndFirstReferenceFlowIntoOwnedProbe()
    {
        using var fixture = new Fixture(); var handler = new Handler(); using var reader = new BoundedSourceHttpReader(handler);
        int probes = 0;
        var inspector = new HlsInitialReferenceInspector(reader, (reference, token) =>
        {
            probes++; Assert.Equal(new byte[] {1,2,3,1,2,3}, File.ReadAllBytes(reference.File.Path));
            Assert.Equal(Encoding.UTF8.GetByteCount(Playlist) + 6, reference.BudgetUsed);
            Assert.Equal(F.Time(0), reference.ActualPlaylistRange.Start); Assert.Equal(F.Time(2), reference.ActualPlaylistRange.End);
            return Task.FromResult(OperationResult<ProbedInitialReference>.Succeeded(Observations(reference)));
        });
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status); Assert.Equal(1, probes);
        Assert.Equal(new[] {"/leaf.m3u8","/init.mp4","/a.m4s"}, handler.Paths);
        Assert.Empty(result.RemainingFiles); Assert.True(File.Exists(result.Data!.Reference.File.Path));
    }
    [Theory]
    [InlineData(OperationStatus.Failed)]
    [InlineData(OperationStatus.Cancelled)]
    public async Task ProbeFailureAndCancellationHandOffOwnedFile(OperationStatus status)
    {
        using var fixture = new Fixture(); using var reader = new BoundedSourceHttpReader(new Handler());
        var inspector = new HlsInitialReferenceInspector(reader, (_, _) => Task.FromResult(status == OperationStatus.Cancelled
            ? OperationResult<ProbedInitialReference>.Cancelled() : OperationResult<ProbedInitialReference>.Failed(
                new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "종료 미확인"))));
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(status, result.Status); Assert.Null(result.Data);
        Assert.True(File.Exists(Assert.Single(result.RemainingFiles).Path)); Assert.NotNull(result.CleanupWarning);
        if (status == OperationStatus.Failed) Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code);
    }
    [Fact]
    public async Task ForeignSnapshotAndTransportStopBeforeHttp()
    {
        using var fixture = new Fixture(); using var other = new Fixture(); var handler = new Handler(); using var reader = new BoundedSourceHttpReader(handler);
        var inspector = new HlsInitialReferenceInspector(reader, (_, _) => throw new InvalidOperationException("Probe must not run"));
        Assert.Equal(OperationErrorCode.VerificationFailed, (await inspector.InspectAsync(fixture.Request, other.Locators, TestContext.Current.CancellationToken)).Error!.Code);
        using var unsupported = new Fixture(transport: FormatTransport.Unknown);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, (await inspector.InspectAsync(unsupported.Request, unsupported.Locators, TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Empty(handler.Paths);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ByteAndSpanBudgetsPreventMediaRequests(bool bytes)
    {
        using var fixture = new Fixture(bytes: bytes ? Encoding.UTF8.GetByteCount(Playlist) : 4096, span: bytes ? 4 : 1);
        var handler = new Handler(); using var reader = new BoundedSourceHttpReader(handler);
        var inspector = new HlsInitialReferenceInspector(reader, (_, _) => throw new InvalidOperationException());
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status); Assert.Single(handler.Paths); Assert.Empty(result.RemainingFiles);
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath));
    }
    [Fact]
    public async Task MissingFragmentDoesNotSkipAndProbeNeverRuns()
    {
        using var fixture = new Fixture();
        var handler = new Handler {Respond = request => request.RequestUri!.AbsolutePath.EndsWith("m3u8", StringComparison.Ordinal)
            ? new(HttpStatusCode.OK) {Content = new StringContent(Playlist)} : new(HttpStatusCode.NotFound)};
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsInitialReferenceInspector(reader, (_, _) => throw new InvalidOperationException()).InspectAsync(
            fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.Data); Assert.Empty(result.RemainingFiles);
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath));
    }
    [Fact]
    public async Task PreCancellationCreatesNeitherHttpNorFiles()
    {
        using var fixture = new Fixture(); var handler = new Handler(); using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsInitialReferenceInspector(reader, (_, _) => throw new InvalidOperationException()).InspectAsync(fixture.Request, fixture.Locators, new(true));
        Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Empty(handler.Paths); Assert.Empty(Directory.GetFiles(fixture.DirectoryPath));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationAfterCollectionPreservesOwnedFileAndTerminationPriority(int behavior)
    {
        using var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        using var reader = new BoundedSourceHttpReader(new Handler());
        var inspector = new HlsInitialReferenceInspector(reader, (reference, token) =>
        {
            cancellation.Cancel();
            if (behavior == 1) throw new OperationCanceledException(token);
            return Task.FromResult(behavior == 2 ? OperationResult<ProbedInitialReference>.Failed(new(
                OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "종료 미확인")) :
                OperationResult<ProbedInitialReference>.Succeeded(Observations(reference)));
        });
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, cancellation.Token);
        Assert.Equal(behavior == 2 ? OperationStatus.Failed : OperationStatus.Cancelled, result.Status);
        Assert.Null(result.Data); Assert.True(File.Exists(Assert.Single(result.RemainingFiles).Path));
        if (behavior == 2) Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code);
    }
    [Fact]
    public async Task ProbeCannotSwapCollectedReferenceIdentity()
    {
        using var fixture = new Fixture(); using var reader = new BoundedSourceHttpReader(new Handler());
        var inspector = new HlsInitialReferenceInspector(reader, (reference, token) =>
        {
            var replacement = new HlsCollectedReference(reference.PlaylistId, reference.File,
                reference.Bytes, reference.BudgetUsed, reference.Sha256, reference.RequestedPlaylistRange, reference.ActualPlaylistRange);
            return Task.FromResult(OperationResult<ProbedInitialReference>.Succeeded(Observations(replacement)));
        });
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code); Assert.Null(result.Data);
        Assert.Single(result.RemainingFiles);
    }
    [Fact]
    public async Task CollectorCleanupFailureIsNotDroppedAtPipelineBoundary()
    {
        using var fixture = new Fixture();
        var handler = new Handler {Respond = request => new(request.RequestUri!.AbsolutePath.EndsWith("m4s", StringComparison.Ordinal)
            ? HttpStatusCode.NotFound : HttpStatusCode.OK) {Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("m3u8", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(Playlist) : [1,2,3])}};
        using var reader = new BoundedSourceHttpReader(handler);
        var collector = new HlsReferenceCollector(reader, deleteOwnedFile: _ => throw new IOException("test cleanup"));
        var inspector = new HlsInitialReferenceInspector(reader, (_, _) => throw new InvalidOperationException(), collector);
        var result = await inspector.InspectAsync(fixture.Request, fixture.Locators, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.Data);
        Assert.True(File.Exists(Assert.Single(result.RemainingFiles).Path)); Assert.NotNull(result.CleanupWarning);
    }
}
