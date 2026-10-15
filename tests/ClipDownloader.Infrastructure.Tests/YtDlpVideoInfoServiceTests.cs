using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class YtDlpVideoInfoServiceTests
{
    private readonly string root = Path.GetFullPath(".");
    private sealed class Locator(string root, bool nodeMissing = false) : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken) =>
            nodeMissing && request.ExecutableName == "node.exe" ? new(ToolLocationStatus.Missing, false, null, null, [], null) :
            new(ToolLocationStatus.Found, false, Path.Combine(root, request.ExecutableName), ToolLocationSource.Path, [], null);
    }
    private sealed class Runner : IProcessRunner
    {
        public List<ProcessRequest> Calls { get; } = [];
        public Func<ProcessRequest, ProcessResult>? Metadata { get; init; }
        public string NodeVersion { get; init; } = "v22.14.0";
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken)
        {
            Assert.Null(outputReceived);
            Calls.Add(request);
            return Task.FromResult(request.Arguments.Contains("--dump-single-json") ? Metadata?.Invoke(request) ?? Result("{\"id\":\"123\",\"formats\":[]}") :
                Result(Path.GetFileName(request.ExecutablePath) == "node.exe" ? NodeVersion : "2026.08.19"));
        }
    }
    private static ProcessResult Result(string text, ProcessStatus status = ProcessStatus.Exited, int code = 0, bool descendants = true) =>
        new(status, code, new(Encoding.UTF8.GetBytes(text), text.Length, false, false, true, true),
            new(Array.Empty<byte>(), 0, false, false, true, true), null, true, true, descendants, null);
    private YtDlpVideoInfoService Service(Runner runner, bool nodeMissing = false)
    {
        var locator = new Locator(root, nodeMissing);
        return new(new ToolExecutionDiagnostics(locator, new(), runner, root), locator, new(), runner, root);
    }
    [Fact]
    public async Task ChzzkUsesCanonicalMetadataOnlyCommandAndBoundedRetries()
    {
        var runner = new Runner();
        var result = await Service(runner).GetVideoInfoAsync(new("https://chzzk.naver.com/video/123?t=50"), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(2, runner.Calls.Count);
        var request = runner.Calls[1];
        Assert.Equal(TimeSpan.FromSeconds(120), request.Timeout);
        Assert.Equal(ProcessOutputMode.Full, request.StandardOutputMode);
        Assert.Equal(ProcessOutputMode.Tail, request.StandardErrorMode);
        foreach (string flag in new[] { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-remote-components", "--simulate", "--skip-download", "--no-playlist", "--abort-on-unavailable-fragments" }) Assert.Contains(flag, request.Arguments);
        Assert.Equal(new[] { "--", "https://chzzk.naver.com/video/123" }, request.Arguments.TakeLast(2));
        Assert.DoesNotContain("--cookies-from-browser", request.Arguments);
        Assert.DoesNotContain("--write-info-json", request.Arguments);
    }
    [Fact]
    public async Task YouTubeRequiresVerifiedExplicitNodeWithoutDownloadingComponents()
    {
        var runner = new Runner { Metadata = _ => Result("{\"id\":\"abcdefghijk\"}") };
        Assert.Equal(OperationStatus.Succeeded, (await Service(runner).GetVideoInfoAsync(new("https://youtu.be/abcdefghijk"), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.Calls[1].Timeout);
        Assert.Contains("node:" + Path.Combine(root, "node.exe"), runner.Calls[2].Arguments);
    }
    [Theory]
    [InlineData("v21.9.0")]
    [InlineData("garbage")]
    [InlineData("v22.14.0 trailing")]
    public async Task UnusableNodeStopsBeforeMetadata(string version)
    {
        var runner = new Runner { NodeVersion = version };
        Assert.Equal(OperationErrorCode.ProcessFailed, (await Service(runner).GetVideoInfoAsync(new("https://youtu.be/abcdefghijk"), TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Equal(2, runner.Calls.Count);
    }
    [Fact]
    public async Task MissingNodeStopsBeforeMetadata()
    {
        var runner = new Runner();
        Assert.Equal(OperationErrorCode.MissingTool, (await Service(runner, true).GetVideoInfoAsync(new("https://youtu.be/abcdefghijk"), TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Single(runner.Calls);
    }
    [Theory]
    [InlineData(ProcessStatus.TimedOut, OperationErrorCode.TimedOut)]
    [InlineData(ProcessStatus.StartFailed, OperationErrorCode.ProcessFailed)]
    [InlineData(ProcessStatus.ExecutionFailed, OperationErrorCode.ProcessFailed)]
    [InlineData(ProcessStatus.TerminationFailed, OperationErrorCode.ProcessTerminationFailed)]
    public async Task ExecutionFailureIsSafeAndNeverRetried(ProcessStatus status, OperationErrorCode code)
    {
        var runner = new Runner { Metadata = _ => Result("SECRET", status, 1) };
        var result = await Service(runner).GetVideoInfoAsync(new("https://chzzk.naver.com/video/123"), TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code); Assert.DoesNotContain("SECRET", result.Error.Message);
        Assert.Equal(2, runner.Calls.Count);
    }
    [Fact]
    public async Task CancellationAndUnconfirmedCleanupRemainDifferent()
    {
        foreach (bool confirmed in new[] { true, false })
        {
            var runner = new Runner { Metadata = _ => Result("", ProcessStatus.Cancelled, 1, confirmed) };
            var result = await Service(runner).GetVideoInfoAsync(new("https://chzzk.naver.com/video/123"), TestContext.Current.CancellationToken);
            if (confirmed) Assert.Equal(OperationStatus.Cancelled, result.Status);
            else Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code);
        }
    }
    [Fact]
    public async Task InvalidUrlAndPreCancellationNeverExecuteTools()
    {
        var runner = new Runner();
        Assert.Equal(OperationStatus.Failed, (await Service(runner).GetVideoInfoAsync(new("https://example.org/"), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(OperationStatus.Cancelled, (await Service(runner).GetVideoInfoAsync(new("https://chzzk.naver.com/video/123"), new(true))).Status);
        Assert.Empty(runner.Calls);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncompleteOrOversizeOutputNeverReachesParser(bool limit)
    {
        var runner = new Runner { Metadata = _ => Result("{}") with { StandardOutput = new(Array.Empty<byte>(), 1, false, limit, false, false) } };
        Assert.Equal(OperationErrorCode.InvalidMetadata, (await Service(runner).GetVideoInfoAsync(new("https://chzzk.naver.com/video/123"), TestContext.Current.CancellationToken)).Error!.Code);
    }
}
