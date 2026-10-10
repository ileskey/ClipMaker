namespace ClipDownloader.Application.Models;

public enum FormatSelectionReason { RequestedBest, RequestedResolution }
public enum FormatSelectionWarning
{
    VideoStreamUnknown, AudioStreamUnknown, AudioSourceRequiresVerification,
    ResolutionUnknown, FrameRateUnknown, VideoCodecUnknown, AudioCodecUnknown
}

// A proposed pair from a metadata snapshot, not proof of acquisition or mode compatibility.
public sealed class FormatSelection
{
    public QualityOption RequestedQuality { get; }
    public VideoFormat VideoFormat { get; }
    public VideoFormat? AudioFormat { get; }
    public FormatSelectionReason Reason { get; }
    public IReadOnlyList<FormatSelectionWarning> Warnings { get; }

    private FormatSelection(QualityOption quality, VideoFormat video, VideoFormat? audio,
        FormatSelectionReason reason, IEnumerable<FormatSelectionWarning> warnings)
    {
        RequestedQuality = quality;
        VideoFormat = video;
        AudioFormat = audio;
        Reason = reason;
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }

    // Validate an explicit proposal. Ranking and original-source audio verification belong to later steps.
    public static OperationResult<FormatSelection> Create(VideoInfo info, QualityOption quality,
        VideoFormat video, VideoFormat? audio, FormatSelectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(video);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if ((quality.Kind == QualityKind.Best) != (reason == FormatSelectionReason.RequestedBest))
            throw new ArgumentException("Selection reason must match the quality request.", nameof(reason));
        if (!IsUniqueMember(info, video) || audio is not null && !IsUniqueMember(info, audio))
            return Invalid("조회한 포맷 목록과 선택한 포맷이 일치하지 않습니다.");
        if (!ValidStreams(video) || audio is not null && !ValidStreams(audio))
            return Invalid("포맷의 스트림 상태를 해석할 수 없습니다.");
        if (video.VideoStream == StreamPresence.Absent)
            return Invalid("영상 스트림이 없는 포맷은 영상 포맷으로 선택할 수 없습니다.");
        if (quality.Kind == QualityKind.Resolution && video.Height != quality.Height)
            return OperationResult<FormatSelection>.Failed(new(OperationErrorCode.UnsupportedFormat,
                OperationErrorCategory.Input, "선택한 포맷의 높이가 요청 해상도와 일치하지 않습니다.", "Quality"));
        if (audio is not null && (audio.Id == video.Id || video.AudioStream != StreamPresence.Absent ||
            audio.VideoStream != StreamPresence.Absent || audio.AudioStream != StreamPresence.Present))
            return Invalid("확인된 영상 전용·음성 전용 포맷 조합이 필요합니다.");

        var warnings = new List<FormatSelectionWarning>();
        if (video.VideoStream == StreamPresence.Unknown) warnings.Add(FormatSelectionWarning.VideoStreamUnknown);
        if (video.AudioStream == StreamPresence.Unknown) warnings.Add(FormatSelectionWarning.AudioStreamUnknown);
        if (video.AudioStream == StreamPresence.Absent && audio is null)
            warnings.Add(FormatSelectionWarning.AudioSourceRequiresVerification);
        if (video.Width is null || video.Height is null) warnings.Add(FormatSelectionWarning.ResolutionUnknown);
        if (video.FrameRate is null) warnings.Add(FormatSelectionWarning.FrameRateUnknown);
        if (video.VideoStream != StreamPresence.Absent && video.VideoCodec is null)
            warnings.Add(FormatSelectionWarning.VideoCodecUnknown);
        if ((audio ?? video).AudioStream != StreamPresence.Absent && (audio ?? video).AudioCodec is null)
            warnings.Add(FormatSelectionWarning.AudioCodecUnknown);
        return OperationResult<FormatSelection>.Succeeded(new(quality, video, audio, reason, warnings));
    }

    private static bool IsUniqueMember(VideoInfo info, VideoFormat format) =>
        !string.IsNullOrWhiteSpace(format.Id) && info.Formats.Count(item => item.Id == format.Id) == 1 &&
        info.Formats.Contains(format);

    private static bool ValidStreams(VideoFormat format) =>
        Enum.IsDefined(format.VideoStream) && Enum.IsDefined(format.AudioStream);

    private static OperationResult<FormatSelection> Invalid(string message) =>
        OperationResult<FormatSelection>.Failed(new(OperationErrorCode.InvalidMetadata, OperationErrorCategory.Execution, message));
}
