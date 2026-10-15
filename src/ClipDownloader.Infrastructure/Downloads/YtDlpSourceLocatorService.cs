using System.Collections.ObjectModel;
using System.Text.Json;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Sources;
using ClipDownloader.Infrastructure.YtDlp;

namespace ClipDownloader.Infrastructure.Downloads;

// Ordinary classes do not print signed URIs/header values via record ToString().
internal sealed class SelectedMediaLocator(VideoFormat format, Uri uri, Uri? manifestUri,
    IReadOnlyDictionary<string, string> headers)
{
    internal VideoFormat Format { get; } = format;
    internal Uri Uri { get; } = uri;
    internal Uri? ManifestUri { get; } = manifestUri;
    internal IReadOnlyDictionary<string, string> Headers { get; } = headers;
}

internal sealed class SelectedSourceLocators(SelectedSourceSnapshot selection, VideoInfo info, SelectedMediaLocator video, SelectedMediaLocator? audio,
    IEnumerable<SelectedMediaLocator>? presentationReferences = null)
{
    internal SelectedSourceSnapshot Selection { get; } = selection;
    internal VideoInfo Info { get; } = info;
    internal SelectedMediaLocator Video { get; } = video;
    internal SelectedMediaLocator? Audio { get; } = audio;
    internal IReadOnlyList<SelectedMediaLocator> PresentationReferences { get; } = Array.AsReadOnly((presentationReferences ?? []).ToArray());
}

internal sealed class YtDlpSourceLocatorService
{
    private readonly YtDlpMetadataQuery query;
    internal YtDlpSourceLocatorService(YtDlpMetadataQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.query = query;
    }

    internal async Task<OperationResult<SelectedSourceLocators>> ResolveAsync(SelectedSourceSnapshot selection, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var result = await query.GetMetadataAsync(selection.Source.PageUrl, token).ConfigureAwait(false);
        if (result.Status == OperationStatus.Cancelled) return OperationResult<SelectedSourceLocators>.Cancelled();
        if (result.Status == OperationStatus.Failed) return OperationResult<SelectedSourceLocators>.Failed(result.Error!);
        if (token.IsCancellationRequested) return OperationResult<SelectedSourceLocators>.Cancelled();
        return Resolve(selection, result.Data!);
    }

    private static OperationResult<SelectedSourceLocators> Resolve(SelectedSourceSnapshot selection, YtDlpMetadataResponse metadata)
    {
        var info = metadata.Info;
        if (info.State != VideoState.Completed) return Fail(OperationErrorCode.SourceUnavailable);
        if (info.Source != selection.Source || info.Duration != selection.MetadataDuration) return Fail(OperationErrorCode.InvalidMetadata);
        var video = info.Formats.SingleOrDefault(format => format.Id == selection.Selection.VideoFormat.Id);
        var audio = selection.Selection.AudioFormat is { } selectedAudio
            ? info.Formats.SingleOrDefault(format => format.Id == selectedAudio.Id) : null;
        if (video is null || selection.Selection.AudioFormat is not null && audio is null) return Fail(OperationErrorCode.UnsupportedFormat);
        if (!SameFormat(selection.Selection.VideoFormat, video) ||
            selection.Selection.AudioFormat is { } expectedAudio && !SameFormat(expectedAudio, audio!))
            return Fail(OperationErrorCode.InvalidMetadata);

        try
        {
            var json = metadata.Json;
            if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (!Unique(root, "webpage_url", out var page)) return Fail(OperationErrorCode.InvalidMetadata);
            if (page.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                if (page.ValueKind != JsonValueKind.String || !Uri.TryCreate(page.GetString(), UriKind.Absolute, out var pageUri))
                    return Fail(OperationErrorCode.InvalidMetadata);
                var source = new SupportedVideoSourceParser().Parse(pageUri);
                if (source.Status != OperationStatus.Succeeded || source.Data != selection.Source) return Fail(OperationErrorCode.InvalidMetadata);
            }
            if (!Unique(root, "formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
                return Fail(OperationErrorCode.InvalidMetadata);
            if (!Unique(root, "http_headers", out var headers)) return Fail(OperationErrorCode.InvalidMetadata);
            var videoLocator = Locator(video, formats, headers);
            if (videoLocator.Status != OperationStatus.Succeeded) return Fail(videoLocator.Error!.Code);
            SelectedMediaLocator? audioLocator = null;
            if (audio is not null)
            {
                var resolved = Locator(audio, formats, headers);
                if (resolved.Status != OperationStatus.Succeeded) return Fail(resolved.Error!.Code);
                audioLocator = resolved.Data!;
            }
            var references = new List<SelectedMediaLocator>();
            // The first declared bridge strategy is deliberately limited to the experimentally verified pair.
            // Supplemental renditions are relation evidence, never replacement output choices.
            if (selection.Source.Platform == VideoPlatform.YouTube && video.Id == "137" && audio?.Id == "140")
            {
                foreach (string id in new[] { "270", "234" })
                {
                    var auxiliary = info.Formats.SingleOrDefault(f => f.Id == id && f.Transport == FormatTransport.Hls);
                    if (auxiliary is null) continue;
                    var resolved = Locator(auxiliary, formats, headers);
                    if (resolved.Status == OperationStatus.Succeeded) references.Add(resolved.Data!);
                }
            }
            return OperationResult<SelectedSourceLocators>.Succeeded(new(selection, info, videoLocator.Data!, audioLocator, references));
        }
        catch (JsonException) { return Fail(OperationErrorCode.InvalidMetadata); }
        catch (ArgumentException) { return Fail(OperationErrorCode.InvalidMetadata); }
    }

    private static bool SameFormat(VideoFormat expected, VideoFormat current) =>
        (expected.FrameRate is null ? current with { FrameRate = null } : current) == expected;

    private static OperationResult<SelectedMediaLocator> Locator(VideoFormat selected, JsonElement formats, JsonElement rootHeaders)
    {
        var matches = formats.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object &&
            Unique(item, "format_id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == selected.Id).ToArray();
        if (matches.Length != 1) return LocatorFail(OperationErrorCode.InvalidMetadata);
        var format = matches[0];
        if (!Unique(format, "url", out var url) || url.ValueKind != JsonValueKind.String || !SafeUri(url.GetString(), out var uri))
            return LocatorFail(OperationErrorCode.InvalidMetadata);
        if (!Unique(format, "manifest_url", out var manifest)) return LocatorFail(OperationErrorCode.InvalidMetadata);
        Uri? manifestUri = null;
        if (manifest.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
            (manifest.ValueKind != JsonValueKind.String || !SafeUri(manifest.GetString(), out manifestUri)))
            return LocatorFail(OperationErrorCode.InvalidMetadata);
        if (!Unique(format, "http_headers", out var formatHeaders)) return LocatorFail(OperationErrorCode.InvalidMetadata);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rootIssue = ReadHeaders(rootHeaders, headers);
        if (rootIssue is { } rootCode) return LocatorFail(rootCode);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var formatIssue = ReadHeaders(formatHeaders, overrides);
        if (formatIssue is { } formatCode) return LocatorFail(formatCode);
        foreach (var header in overrides) headers[header.Key] = header.Value;
        return OperationResult<SelectedMediaLocator>.Succeeded(new(selected, uri!, manifestUri,
            new ReadOnlyDictionary<string, string>(headers)));
    }

    private static OperationErrorCode? ReadHeaders(JsonElement value, Dictionary<string, string> headers)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) return OperationErrorCode.InvalidMetadata;
        foreach (var item in value.EnumerateObject())
        {
            if (!SourceHttpRequest.IsSupportedHeader(item.Name)) return OperationErrorCode.UnsupportedFormat;
            if (item.Value.ValueKind != JsonValueKind.String) return OperationErrorCode.InvalidMetadata;
            string text = item.Value.GetString()!;
            if (text.Contains('\r') || text.Contains('\n') || !headers.TryAdd(item.Name, text)) return OperationErrorCode.InvalidMetadata;
        }
        return null;
    }

    private static bool SafeUri(string? text, out Uri? uri)
    {
        uri = null;
        return text is not null && !text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) &&
            Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }
    private static bool Unique(JsonElement item, string name, out JsonElement value)
    {
        value = default;
        bool found = false;
        foreach (var property in item.EnumerateObject())
        {
            if (property.Name != name) continue;
            if (found) return false;
            found = true;
            value = property.Value;
        }
        return true;
    }
    private static OperationResult<SelectedSourceLocators> Fail(OperationErrorCode code) =>
        OperationResult<SelectedSourceLocators>.Failed(Error(code));
    private static OperationResult<SelectedMediaLocator> LocatorFail(OperationErrorCode code) =>
        OperationResult<SelectedMediaLocator>.Failed(Error(code));
    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        "선택한 source의 미디어 주소·headers 또는 동일한 선택 상태를 확인하지 못했습니다.");
}
