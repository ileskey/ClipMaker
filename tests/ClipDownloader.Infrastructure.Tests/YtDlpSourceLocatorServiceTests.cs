using System.Text;
using System.Text.Json.Nodes;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipDownloader.Infrastructure.Downloads;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class YtDlpSourceLocatorServiceTests
{
    private const string secret = "private-locator-test-value";
    private static string Json(bool split = false, bool unknownFps = false) => $$$"""
        {"id":"15430558","live_status":"was_live","duration":120,
         "webpage_url":"https://chzzk.naver.com/video/15430558",
         "http_headers":{"User-Agent":"root-agent","Accept":"*/*","Sec-Fetch-Mode":"navigate"},
         "formats":[{"format_id":"v","width":1920,"height":1080,"fps":{{{(unknownFps ? "null" : "30")}}},
          "vcodec":"h264","acodec":"{{{(split ? "none" : "aac")}}}","protocol":"m3u8_native",
          "url":"https://media.example/video?token={{{secret}}}",
          "manifest_url":"https://media.example/master?token={{{secret}}}",
          "http_headers":{"user-agent":"format-agent"}}
          {{{(split ? ",{\"format_id\":\"a\",\"vcodec\":\"none\",\"acodec\":\"aac\",\"protocol\":\"https\",\"url\":\"https://media.example/audio?token=" + secret + "\"}" : "")}}}]}
        """;

    private static SelectedSourceSnapshot Snapshot(string json)
    {
        Assert.True(VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "15430558", out var source));
        var parsed = new YtDlpMetadataParser().Parse(Encoding.UTF8.GetBytes(json), source);
        Assert.Equal(OperationStatus.Succeeded, parsed.Status);
        var video = parsed.Data!.Formats.Single(format => format.Id == "v");
        var audio = parsed.Data.Formats.SingleOrDefault(format => format.Id == "a");
        var selection = FormatSelection.Create(parsed.Data, QualityOption.Best, video, audio, FormatSelectionReason.RequestedBest);
        Assert.Equal(OperationStatus.Succeeded, selection.Status);
        var snapshot = SelectedSourceSnapshot.Create(Guid.NewGuid(), parsed.Data, selection.Data!, ClipMode.Fast);
        Assert.Equal(OperationStatus.Succeeded, snapshot.Status);
        return snapshot.Data!;
    }

    private sealed class Locator : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken token) =>
            new(ToolLocationStatus.Found, false, Path.GetFullPath(request.ExecutableName), ToolLocationSource.Path, [], null);
    }
    private sealed class Runner(string json, ProcessStatus status = ProcessStatus.Exited, bool complete = true) : IProcessRunner
    {
        internal List<ProcessRequest> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? callback, CancellationToken token)
        {
            Calls.Add(request);
            bool metadata = request.Arguments.Contains("--dump-single-json");
            byte[] bytes = Encoding.UTF8.GetBytes(metadata ? json : "2026.08.19");
            return Task.FromResult(new ProcessResult(metadata ? status : ProcessStatus.Exited, metadata && status != ProcessStatus.Exited ? 1 : 0,
                new(bytes, bytes.Length, false, false, !metadata || complete, !metadata || complete),
                new(Array.Empty<byte>(), 0, false, false, true, true), null, true, true, status != ProcessStatus.TerminationFailed, null));
        }
    }
    private static YtDlpSourceLocatorService Service(Runner runner)
    {
        var locator = new Locator();
        var settings = new ToolPathSettings();
        return new(new(new ToolExecutionDiagnostics(locator, settings, runner, Path.GetFullPath(".")), locator,
            settings, runner, Path.GetFullPath(".")));
    }

    [Fact]
    public async Task ResolvesOnlyExplicitPairAndMergesHeadersWithoutLeakingToPublicInfo()
    {
        string json = Json(split: true);
        var expected = Snapshot(json);
        var runner = new Runner(json);
        var result = await Service(runner).ResolveAsync(expected, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var data = result.Data!;
        Assert.Same(expected, data.Selection);
        Assert.Equal(expected.Source, data.Info.Source);
        Assert.Equal(2, data.Info.Formats.Count);
        Assert.Contains(secret, data.Video.Uri.Query);
        Assert.NotNull(data.Video.ManifestUri);
        Assert.NotNull(data.Audio);
        Assert.Equal("format-agent", data.Video.Headers["User-Agent"]);
        Assert.Equal("root-agent", data.Audio.Headers["user-agent"]);
        Assert.Equal("navigate", data.Video.Headers["Sec-Fetch-Mode"]);
        var request = new SourceHttpRequest(data.Video.Uri, SourceHttpMode.Resource, 1024, headers: data.Video.Headers);
        Assert.Equal("format-agent", request.Headers["user-agent"]);
        Assert.DoesNotContain(secret, data.ToString()!);
        Assert.DoesNotContain(secret, data.Video.ToString()!);
        Assert.DoesNotContain(secret, JsonSerializerSafe(expected));
        Assert.Equal(2, runner.Calls.Count);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("--write-info-json"));
    }
    private static string JsonSerializerSafe(SelectedSourceSnapshot value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public async Task InitialUnknownMetadataFpsMayResolveWithoutChangingPreparedSnapshot()
    {
        var snapshot = Snapshot(Json(unknownFps: true));
        var result = await Service(new(Json())).ResolveAsync(snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Null(snapshot.Selection.VideoFormat.FrameRate);
        Assert.Equal(30, result.Data!.Video.Format.FrameRate!.Numerator);
    }

    [Theory]
    [InlineData("fps", "60")]
    [InlineData("fps", "null")]
    [InlineData("height", "720")]
    [InlineData("vcodec", "\"vp9\"")]
    [InlineData("protocol", "\"https\"")]
    public async Task ChangedSelectedFormatIsRejectedWithoutRankingFallback(string key, string value)
    {
        string original = Json();
        var changed = JsonNode.Parse(original)!;
        changed["formats"]![0]![key] = JsonNode.Parse(value);
        var result = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
        Assert.Null(result.Data);
        Assert.DoesNotContain(secret, result.Error.Message);
    }

    [Theory]
    [InlineData("id", "\"15430559\"", OperationErrorCode.InvalidMetadata)]
    [InlineData("webpage_url", "\"https://chzzk.naver.com/video/15430559\"", OperationErrorCode.InvalidMetadata)]
    [InlineData("duration", "121", OperationErrorCode.InvalidMetadata)]
    [InlineData("live_status", "\"is_live\"", OperationErrorCode.SourceUnavailable)]
    public async Task SourceIdentityDurationAndCompletedStateAreRevalidated(string key, string value, OperationErrorCode code)
    {
        string original = Json();
        var changed = JsonNode.Parse(original)!;
        changed[key] = JsonNode.Parse(value);
        var result = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code);
    }

    [Theory]
    [InlineData("\"http://media.example/asset\"")]
    [InlineData("\"https://user:pass@media.example/asset\"")]
    [InlineData("\"https://media.example/asset#fragment\"")]
    [InlineData("null")]
    [InlineData("123")]
    public async Task UnsafeOrMalformedLocatorDoesNotEscapeAsDiagnostic(string value)
    {
        string original = Json();
        var changed = JsonNode.Parse(original)!;
        changed["formats"]![0]!["url"] = JsonNode.Parse(value);
        var result = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
        Assert.DoesNotContain("media.example", result.Error.Message);
        Assert.DoesNotContain(secret, result.Error.Message);
    }

    [Theory]
    [InlineData("{\"Cookie\":\"private-value\"}", OperationErrorCode.UnsupportedFormat)]
    [InlineData("{\"Authorization\":\"private-value\"}", OperationErrorCode.UnsupportedFormat)]
    [InlineData("{\"X-Required\":\"private-value\"}", OperationErrorCode.UnsupportedFormat)]
    [InlineData("{\"User-Agent\":5}", OperationErrorCode.InvalidMetadata)]
    [InlineData("{\"User-Agent\":\"bad\\r\\nheader\"}", OperationErrorCode.InvalidMetadata)]
    [InlineData("{\"User-Agent\":\"one\",\"user-agent\":\"two\"}", OperationErrorCode.InvalidMetadata)]
    public async Task UnsupportedHeadersAreNotSilentlyDroppedAndInvalidHeadersFailSafely(string headers, OperationErrorCode code)
    {
        string original = Json();
        var changed = JsonNode.Parse(original)!;
        changed["http_headers"] = JsonNode.Parse(headers);
        var result = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code);
        Assert.DoesNotContain("private-value", result.Error.Message);
    }

    [Fact]
    public async Task MissingAudioAndDuplicateUrlNeverUseAnotherFormatOrRootFallback()
    {
        string original = Json(split: true);
        var changed = JsonNode.Parse(original)!;
        changed["formats"]!.AsArray().RemoveAt(1);
        var absent = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, absent.Error!.Code);
        string duplicate = Json().Replace("\"url\":\"https://media.example/video", "\"url\":\"https://other.example/\",\"url\":\"https://media.example/video", StringComparison.Ordinal);
        var invalid = await Service(new(duplicate)).ResolveAsync(Snapshot(Json()), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, invalid.Error!.Code);
        var missing = JsonNode.Parse(Json())!;
        missing["url"] = "https://other.example/root-fallback";
        missing["formats"]![0]!.AsObject().Remove("url");
        var noFallback = await Service(new(missing.ToJsonString())).ResolveAsync(Snapshot(Json()), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, noFallback.Error!.Code);
    }

    [Fact]
    public async Task ChangedSignedLocatorIsAllowedOnlyAfterSameSelectionValidation()
    {
        string original = Json();
        var changed = JsonNode.Parse(original)!;
        changed["formats"]![0]!["url"] = "https://media.example/video?token=renewed-private-value";
        var result = await Service(new(changed.ToJsonString())).ResolveAsync(Snapshot(original), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal("?token=renewed-private-value", result.Data!.Video.Uri.Query);
        Assert.DoesNotContain("renewed-private-value", result.Data.Video.ToString()!);
    }

    [Theory]
    [InlineData(ProcessStatus.TimedOut, OperationErrorCode.TimedOut)]
    [InlineData(ProcessStatus.TerminationFailed, OperationErrorCode.ProcessTerminationFailed)]
    public async Task QueryFailurePreservesExecutionCauseWithoutLocatorData(ProcessStatus status, OperationErrorCode code)
    {
        var result = await Service(new(Json(), status)).ResolveAsync(Snapshot(Json()), TestContext.Current.CancellationToken);
        Assert.Equal(code, result.Error!.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task CancelledOrIncompleteQueriesNeverResolveLocators()
    {
        var runner = new Runner(Json());
        var cancelled = await Service(runner).ResolveAsync(Snapshot(Json()), new(true));
        Assert.Equal(OperationStatus.Cancelled, cancelled.Status);
        Assert.Empty(runner.Calls);
        var incomplete = await Service(new(Json(), complete: false)).ResolveAsync(Snapshot(Json()), TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, incomplete.Error!.Code);
        var toolCancelled = await Service(new(Json(), ProcessStatus.Cancelled)).ResolveAsync(Snapshot(Json()), TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Cancelled, toolCancelled.Status);
    }
}
