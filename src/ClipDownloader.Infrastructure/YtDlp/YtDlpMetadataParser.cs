using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.YtDlp;

public sealed class YtDlpMetadataParser
{
    public OperationResult<VideoInfo> Parse(ReadOnlyMemory<byte> json, VideoSourceReference source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (json.Length > 32 * 1024 * 1024) return Invalid();
        if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(json.Span);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Unique(root, "id", out var id) ||
                id.ValueKind != JsonValueKind.String || id.GetString() != source.Id) return Invalid();
            if (!Unique(root, "_type", out var type) || type.ValueKind != JsonValueKind.Undefined &&
                (type.ValueKind != JsonValueKind.String || type.GetString() != "video") || root.TryGetProperty("entries", out _)) return Invalid();
            if (!Unique(root, "has_drm", out var drm)) return Invalid();
            if (drm.ValueKind == JsonValueKind.True)
                return OperationResult<VideoInfo>.Failed(new(OperationErrorCode.SourceUnavailable, OperationErrorCategory.Execution,
                    "DRM 콘텐츠는 지원하지 않습니다."));
            var issues = new List<MetadataIssue>();
            string? title = Text(Field(root, "title", issues), "title", issues);
            JsonElement uploaderField = Field(root, "uploader", issues);
            string? uploader = Text(uploaderField, "uploader", issues);
            if (Unique(root, "uploader", out _) && uploaderField.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                uploader = Text(Field(root, "channel", issues), "channel", issues);
            JsonElement durationField = Field(root, "duration", issues);
            TimeSpan? duration = MetadataNumbers.Duration(durationField, out bool rounded);
            if (duration is null) InvalidOptional(durationField, "duration", issues);
            else if (rounded) issues.Add(new("duration", MetadataIssueReason.Rounded));
            Uri? thumbnail = Thumbnail(Field(root, "thumbnail", issues), issues);
            string? date = Text(Field(root, "upload_date", issues), "upload_date", issues);
            DateOnly? uploadDate = date is not null && date.Length == 8 && date.All(char.IsAsciiDigit) &&
                DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate : null;
            if (date is not null && uploadDate is null) issues.Add(new("upload_date", MetadataIssueReason.InvalidValue));
            VideoState state = State(root, issues);
            if (!Unique(root, "formats", out var formatsField)) return Invalid();
            var formats = Formats(formatsField, issues);
            return OperationResult<VideoInfo>.Succeeded(new(source, title, duration, uploader, thumbnail, uploadDate, state, formats, issues));
        }
        catch (JsonException) { return Invalid(); }
        catch (DecoderFallbackException) { return Invalid(); }
    }

    private static List<VideoFormat> Formats(JsonElement field, List<MetadataIssue> issues)
    {
        var output = new List<VideoFormat>();
        if (field.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("formats", field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? MetadataIssueReason.Missing : MetadataIssueReason.InvalidType));
            return output;
        }
        var candidates = new List<(JsonElement Value, string Id, int Index)>();
        int index = 0;
        foreach (var item in field.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !Unique(item, "format_id", out var id) ||
                id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                issues.Add(new("format_id", MetadataIssueReason.InvalidValue, index));
            else candidates.Add((item, id.GetString()!, index));
            index++;
        }
        var duplicates = candidates.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in candidates)
        {
            if (duplicates.Contains(item.Id)) { issues.Add(new("format_id", MetadataIssueReason.DuplicateFormat, item.Index)); continue; }
            if (!Unique(item.Value, "has_drm", out var drm))
            { issues.Add(new("has_drm", MetadataIssueReason.Ambiguous, item.Index)); continue; }
            if (drm.ValueKind == JsonValueKind.True)
            { issues.Add(new("has_drm", MetadataIssueReason.Unsupported, item.Index)); continue; }
            var (videoCodec, video) = Codec(Field(item.Value, "vcodec", issues, item.Index), "vcodec", issues, item.Index);
            var (audioCodec, audio) = Codec(Field(item.Value, "acodec", issues, item.Index), "acodec", issues, item.Index);
            if (video == StreamPresence.Absent && audio == StreamPresence.Absent)
            { issues.Add(new("streams", MetadataIssueReason.Unsupported, item.Index)); continue; }
            var widthField = Field(item.Value, "width", issues, item.Index);
            var heightField = Field(item.Value, "height", issues, item.Index);
            var fpsField = Field(item.Value, "fps", issues, item.Index);
            int? width = MetadataNumbers.Dimension(widthField);
            int? height = MetadataNumbers.Dimension(heightField);
            var fps = MetadataNumbers.FrameRate(fpsField);
            if (width is null) InvalidOptional(widthField, "width", issues, item.Index);
            if (height is null) InvalidOptional(heightField, "height", issues, item.Index);
            if (fps is null) InvalidOptional(fpsField, "fps", issues, item.Index);
            output.Add(new(item.Id, width, height, fps, videoCodec, audioCodec, video, audio));
        }
        return output;
    }

    private static (string? Codec, StreamPresence Presence) Codec(JsonElement value, string name, List<MetadataIssue> issues, int index)
    {
        string? codec = Text(value, name, issues, index);
        return codec is null ? (null, StreamPresence.Unknown) : codec == "none" ? (null, StreamPresence.Absent) : (codec, StreamPresence.Present);
    }

    private static VideoState State(JsonElement root, List<MetadataIssue> issues)
    {
        string? status = Text(Field(root, "live_status", issues), "live_status", issues);
        VideoState state = status switch
        {
            "not_live" or "was_live" => VideoState.Completed,
            "is_live" => VideoState.Live, "is_upcoming" => VideoState.Upcoming, _ => VideoState.Unknown
        };
        JsonElement isLive = Field(root, "is_live", issues);
        if (isLive.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False))
            issues.Add(new("is_live", MetadataIssueReason.InvalidType));
        if (isLive.ValueKind == JsonValueKind.False && state == VideoState.Live)
        { issues.Add(new("live_status", MetadataIssueReason.Ambiguous)); return VideoState.Unknown; }
        if (isLive.ValueKind == JsonValueKind.True && state is VideoState.Completed or VideoState.Upcoming)
        { issues.Add(new("live_status", MetadataIssueReason.Ambiguous)); return VideoState.Unknown; }
        if (isLive.ValueKind == JsonValueKind.True && state == VideoState.Unknown) return VideoState.Live;
        if (status is not null && state == VideoState.Unknown) issues.Add(new("live_status", MetadataIssueReason.InvalidValue));
        return state;
    }

    private static Uri? Thumbnail(JsonElement value, List<MetadataIssue> issues)
    {
        string? text = Text(value, "thumbnail", issues);
        if (text is null) return null;
        if (text.Any(char.IsWhiteSpace) || text.Contains('\\') || !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !uri.IsWellFormedOriginalString() || uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns ||
            uri.UserInfo.Length != 0 || text.Contains('@') || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        { issues.Add(new("thumbnail", MetadataIssueReason.UnsafeReference)); return null; }
        return uri;
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
    private static JsonElement Field(JsonElement item, string name, List<MetadataIssue> issues, int? index = null)
    {
        if (Unique(item, name, out var value)) return value;
        issues.Add(new(name, MetadataIssueReason.Ambiguous, index));
        return default;
    }
    private static string? Text(JsonElement value, string name, List<MetadataIssue> issues, int? index = null)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
        issues.Add(new(name, value.ValueKind == JsonValueKind.String ? MetadataIssueReason.InvalidValue : MetadataIssueReason.InvalidType, index));
        return null;
    }
    private static void InvalidOptional(JsonElement value, string name, List<MetadataIssue> issues, int? index = null)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        issues.Add(new(name, value.ValueKind == JsonValueKind.Number ? MetadataIssueReason.OutOfRange : MetadataIssueReason.InvalidType, index));
    }
    private static OperationResult<VideoInfo> Invalid() => OperationResult<VideoInfo>.Failed(
        new(OperationErrorCode.InvalidMetadata, OperationErrorCategory.Execution, "영상 메타데이터 응답을 사용할 수 없습니다."));
}
