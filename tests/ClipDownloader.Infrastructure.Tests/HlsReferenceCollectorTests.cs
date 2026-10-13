using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsReferenceCollectorTests
{
    private const string text = "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:2,\na.m4s\n#EXTINF:2,\nb.m4s\n#EXT-X-ENDLIST\n";
    private sealed class Workspace : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(".artifacts"), "hls-collector-tests", Guid.NewGuid().ToString("N"));
        internal Workspace() => Directory.CreateDirectory(DirectoryPath);
        // Only this fixture's newly-created directory and immediate files are removed.
        public void Dispose()
        {
            foreach (string file in Directory.GetFiles(DirectoryPath)) File.Delete(file);
            Directory.Delete(DirectoryPath);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal List<Uri> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls.Add(request.RequestUri!); return Task.FromResult(response(request)); }
    }
    private static SignedMediaTime Time(long n)
    {
        Assert.True(SignedMediaTime.TryCreate(n, 1, out var time, out _)); return time;
    }
    private static (HlsMediaPlaylist Playlist, HlsReferencePlan Plan, SelectedMediaLocator Locator) Inputs(string suffix = "selected")
    {
        var uri = new Uri("https://media.example/" + suffix + ".m3u8?token=private-test-value");
        var parsed = new HlsMediaPlaylistParser().Parse(Encoding.UTF8.GetBytes(text), uri);
        Assert.Equal(OperationStatus.Succeeded, parsed.Status);
        Assert.True(PresentationRange.TryCreate(Time(0), Time(4), out var range, out _));
        var planned = new HlsReferencePlanner().Plan(parsed.Data!, range, Time(4), 2);
        Assert.Equal(OperationStatus.Succeeded, planned.Status);
        var format = new VideoFormat("hls-selected", 1920, 1080, null, "h264", "aac", StreamPresence.Present,
            StreamPresence.Present, Transport: FormatTransport.Hls);
        return (parsed.Data!, planned.Data!, new(format, uri, null, new Dictionary<string, string>()));
    }
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class FaultStream(Stream inner, bool writeFails, bool disposeFails) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (writeFails) throw new IOException("private write detail");
            return inner.WriteAsync(buffer, token);
        }
        public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            if (disposeFails) throw new IOException("private dispose detail");
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task WriteFailureRemovesOnlyNewFileAndCannotReturnCandidate()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        using var reader = new BoundedSourceHttpReader(new Handler(_ => Bytes([1, 2, 3])));
        var collector = new HlsReferenceCollector(reader, path => new FaultStream(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), true, false));
        var result = await collector.CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.OutputFailed, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
        Assert.DoesNotContain("private write detail", result.Error.Message);
    }

    [Fact]
    public async Task CloseFailureCannotPublishAnOtherwiseCompleteCandidate()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        using var reader = new BoundedSourceHttpReader(new Handler(_ => Bytes([1, 2, 3])));
        bool deleted = false;
        var collector = new HlsReferenceCollector(reader, path => new FaultStream(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), false, true), _ => deleted = true);
        var result = await collector.CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal(OperationErrorCode.OutputFailed, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.Single(result.RemainingFiles);
        Assert.False(deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainCloseRetainsPathWithoutReplacingOriginalFailureOrCancellation(bool cancel)
    {
        using var workspace = new Workspace();
        var input = Inputs();
        using var stop = new CancellationTokenSource();
        using var reader = new BoundedSourceHttpReader(new Handler(_ =>
        {
            if (cancel) stop.Cancel();
            return new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
        }));
        bool deleted = false;
        var collector = new HlsReferenceCollector(reader, path => new FaultStream(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None), false, true),
            _ => deleted = true);
        var result = await collector.CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, stop.Token);
        Assert.Null(result.Data);
        if (cancel) { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Error); }
        else Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
        Assert.False(deleted);
        Assert.True(File.Exists(Assert.Single(result.RemainingFiles).Path));
        Assert.NotNull(result.CleanupWarning);
    }

    [Fact]
    public async Task CollectsInitAndSelectedSegmentsWithHashCountsAndOwnedProvenance()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        var handler = new Handler(request => Bytes(request.RequestUri!.AbsolutePath switch
        { "/init.mp4" => [1, 2, 3], "/a.m4s" => [4, 5], "/b.m4s" => [6, 7, 8], _ => throw new InvalidOperationException() }));
        using var reader = new BoundedSourceHttpReader(handler);
        Guid id = Guid.NewGuid();
        var result = await new HlsReferenceCollector(reader).CollectAsync(id, input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(input.Playlist.SourceBytes + 8, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Null(result.Error);
        Assert.Empty(result.RemainingFiles);
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8];
        Assert.Equal(expected, await File.ReadAllBytesAsync(result.Data!.File.Path, TestContext.Current.CancellationToken));
        Assert.Equal(8, result.Data.Bytes);
        Assert.Equal(input.Playlist.SourceBytes + 8, result.Data.BudgetUsed);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), result.Data.Sha256);
        Assert.Equal(result.Data.Bytes, result.Data.File.Content!.Bytes);
        Assert.Equal(result.Data.Sha256, result.Data.File.Content.Sha256);
        Assert.Equal(id, result.Data.File.Id);
        Assert.Equal("hls-selected", Assert.Single(result.Data.File.SourceFormatIds));
        Assert.Equal(input.Playlist.Id, result.Data.PlaylistId);
        Assert.Equal(3, handler.Calls.Count);
        Assert.Equal(new[] { "/init.mp4", "/a.m4s", "/b.m4s" }, handler.Calls.Select(uri => uri.AbsolutePath));
    }

    [Fact]
    public async Task BudgetCountsPlaylistAndStopsBeforeFetchingPastExhaustion()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        var handler = new Handler(_ => Bytes([1, 2, 3]));
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsReferenceCollector(reader).CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(input.Playlist.SourceBytes + 3, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.Single(handler.Calls);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new HlsReferenceCollector(reader).CollectAsync(Guid.NewGuid(),
            input.Playlist, input.Plan, input.Locator, workspace.DirectoryPath, new(10000, Time(4)), 0, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrEmptySegmentFailsWithoutSkippingAndRemovesOwnedPartial(bool missing)
    {
        using var workspace = new Workspace();
        var input = Inputs();
        var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/a.m4s"
            ? missing ? new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) } : Bytes([])
            : Bytes([1, 2, 3]));
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsReferenceCollector(reader).CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
        Assert.DoesNotContain("private-test-value", result.Error.Message);
    }

    [Fact]
    public async Task CancellationPreservesUserFileAndClearsOnlyNewPartial()
    {
        using var workspace = new Workspace();
        string userFile = Path.Combine(workspace.DirectoryPath, "user.txt");
        await File.WriteAllTextAsync(userFile, "preserve", TestContext.Current.CancellationToken);
        var input = Inputs();
        using var stop = new CancellationTokenSource();
        var handler = new Handler(request => { if (request.RequestUri!.AbsolutePath == "/a.m4s") stop.Cancel(); return Bytes([1, 2, 3]); });
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsReferenceCollector(reader).CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, stop.Token);
        Assert.Equal(OperationStatus.Cancelled, result.Status);
        Assert.Null(result.Error);
        Assert.Null(result.Data);
        Assert.Equal("preserve", await File.ReadAllTextAsync(userFile, TestContext.Current.CancellationToken));
        Assert.Equal(userFile, Assert.Single(Directory.GetFiles(workspace.DirectoryPath)));
    }

    [Fact]
    public async Task CreateNewConflictDoesNotFetchOverwriteOrDeleteExistingFile()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        Guid id = Guid.NewGuid();
        string existing = Path.Combine(workspace.DirectoryPath, $"hls-ref-{id:N}.mp4");
        await File.WriteAllTextAsync(existing, "user", TestContext.Current.CancellationToken);
        var handler = new Handler(_ => Bytes([1]));
        using var reader = new BoundedSourceHttpReader(handler);
        var result = await new HlsReferenceCollector(reader).CollectAsync(id, input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.OutputConflict, result.Error!.Code);
        Assert.Empty(handler.Calls);
        Assert.Equal("user", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupFailureRetainsOwnedPathAndOriginalFailure()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        using var reader = new BoundedSourceHttpReader(new Handler(_ => new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) }));
        var collector = new HlsReferenceCollector(reader, deleteOwnedFile: _ => throw new IOException("private cleanup detail"));
        var result = await collector.CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
        Assert.True(File.Exists(Assert.Single(result.RemainingFiles).Path));
        Assert.NotNull(result.CleanupWarning);
        Assert.DoesNotContain("private cleanup detail", result.CleanupWarning);
    }

    [Fact]
    public async Task ForeignOrEditedPlanAndSpanBudgetAreRejectedBeforeAnyFileOrHttp()
    {
        using var workspace = new Workspace();
        var input = Inputs();
        var other = Inputs("other");
        var handler = new Handler(_ => Bytes([1]));
        using var reader = new BoundedSourceHttpReader(handler);
        var collector = new HlsReferenceCollector(reader);
        var foreign = await collector.CollectAsync(Guid.NewGuid(), other.Playlist, input.Plan, other.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), other.Playlist.SourceBytes, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed, foreign.Error!.Code);
        var edited = new HlsReferencePlan(input.Playlist.Id, input.Plan.Initialization, [input.Plan.Segments[1]], input.Plan.RequestedPlaylistRange);
        Assert.Equal(OperationErrorCode.VerificationFailed, (await collector.CollectAsync(Guid.NewGuid(), input.Playlist, edited, input.Locator,
            workspace.DirectoryPath, new(10000, Time(4)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, (await collector.CollectAsync(Guid.NewGuid(), input.Playlist, input.Plan, input.Locator,
            workspace.DirectoryPath, new(10000, Time(3)), input.Playlist.SourceBytes, TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Empty(handler.Calls);
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath));
    }
}
