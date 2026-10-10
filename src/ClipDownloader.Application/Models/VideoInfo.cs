using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

public enum VideoState { Unknown, Completed, Live, Upcoming }
public enum StreamPresence { Unknown, Present, Absent }
public enum FormatTransport { Unknown, Https, Hls, Other }
public enum MetadataIssueReason { Missing, InvalidType, InvalidValue, OutOfRange, Ambiguous, DuplicateFormat, Unsupported, UnsafeReference, Rounded }
public sealed record MetadataIssue(string Field, MetadataIssueReason Reason, int? FormatIndex = null);

public sealed record VideoFormat(
    string Id, int? Width, int? Height, FrameRate? FrameRate,
    string? VideoCodec, string? AudioCodec, StreamPresence VideoStream, StreamPresence AudioStream,
    decimal? VideoBitrateKbps = null, decimal? AudioBitrateKbps = null,
    FormatTransport Transport = FormatTransport.Unknown);

public sealed class VideoInfo
{
    public VideoSourceReference Source { get; }
    public string? Title { get; }
    public TimeSpan? Duration { get; }
    public string? Uploader { get; }
    public Uri? ThumbnailUrl { get; }
    public DateOnly? UploadDate { get; }
    public VideoState State { get; }
    public IReadOnlyList<VideoFormat> Formats { get; }
    public IReadOnlyList<MetadataIssue> Issues { get; }

    public VideoInfo(VideoSourceReference source, string? title, TimeSpan? duration, string? uploader,
        Uri? thumbnailUrl, DateOnly? uploadDate, VideoState state,
        IEnumerable<VideoFormat> formats, IEnumerable<MetadataIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(issues);
        Source = source;
        Title = title;
        Duration = duration;
        Uploader = uploader;
        ThumbnailUrl = thumbnailUrl;
        UploadDate = uploadDate;
        State = state;
        Formats = Array.AsReadOnly(formats.ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
    }
}
