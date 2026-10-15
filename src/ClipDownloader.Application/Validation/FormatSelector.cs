using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Validation;

// Declared metadata ranking only. Mode compatibility and source audio still require execution checks.
public sealed class FormatSelector
{
    public OperationResult<FormatSelection> Select(VideoInfo info, QualityOption quality)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(quality);
        if (info.Formats.Any(f => f is null || string.IsNullOrWhiteSpace(f.Id) ||
            !Enum.IsDefined(f.VideoStream) || !Enum.IsDefined(f.AudioStream) || !Enum.IsDefined(f.Transport) ||
            f.VideoBitrateKbps is <= 0 || f.AudioBitrateKbps is <= 0) ||
            info.Formats.GroupBy(f => f.Id, StringComparer.Ordinal).Any(g => g.Count() != 1))
            return Failed(OperationErrorCode.InvalidMetadata, OperationErrorCategory.Execution, "포맷 목록의 식별자 또는 스트림 상태가 잘못되었습니다.");

        var videos = info.Formats.Where(f => f.VideoStream != StreamPresence.Absent).ToArray();
        var candidates = videos.Where(f => f.Height > 0 && (quality.Kind == QualityKind.Best || f.Height == quality.Height))
            .OrderByDescending(f => f, Comparer<VideoFormat>.Create(CompareVideo))
            .ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0)
            return quality.Kind == QualityKind.Resolution
                ? Failed(OperationErrorCode.UnsupportedFormat, OperationErrorCategory.Input, "요청한 높이의 영상 포맷을 제공하지 않습니다.", "Quality")
                : Failed(OperationErrorCode.UnsupportedFormat, OperationErrorCategory.Execution, "높이를 확인한 영상 포맷이 없어 Best를 선택할 수 없습니다.");
        VideoFormat selected = candidates[0];
        var warnings = new List<FormatSelectionWarning> { FormatSelectionWarning.DeclaredQualityOnly };
        if (videos.Any(f => f.Height is null or <= 0 || f.Width is null or <= 0 || f.FrameRate is null ||
            f.VideoCodec is null || f.VideoStream == StreamPresence.Unknown || f.VideoBitrateKbps is null ||
            f.Transport == FormatTransport.Unknown))
            warnings.Add(FormatSelectionWarning.RankingMetadataIncomplete);
        if (candidates.Length > 1 && CompareVideo(selected, candidates[1]) == 0)
            warnings.Add(FormatSelectionWarning.StableIdTieBreak);

        VideoFormat? audio = null;
        if (selected.AudioStream == StreamPresence.Absent)
        {
            var audios = info.Formats.Where(f => f.VideoStream == StreamPresence.Absent && f.AudioStream == StreamPresence.Present)
                .OrderByDescending(f => f, Comparer<VideoFormat>.Create(CompareAudio))
                .ThenBy(f => f.Id, StringComparer.Ordinal).ToArray();
            if (audios.Length != 0)
            {
                audio = audios[0];
                if (audios.Any(f => f.AudioCodec is null || f.AudioBitrateKbps is null || f.Transport == FormatTransport.Unknown))
                    warnings.Add(FormatSelectionWarning.RankingMetadataIncomplete);
                if (audios.Length > 1 && CompareAudio(audio, audios[1]) == 0)
                    warnings.Add(FormatSelectionWarning.StableIdTieBreak);
            }
        }
        FormatSelectionCriterion[] criteria = audio is null
            ? [FormatSelectionCriterion.Height, FormatSelectionCriterion.FrameRate, FormatSelectionCriterion.Width,
                FormatSelectionCriterion.VideoCodecPreference, FormatSelectionCriterion.TransportPreference,
                FormatSelectionCriterion.VideoBitrateEstimate, FormatSelectionCriterion.EmbeddedAudioPresence, FormatSelectionCriterion.OrdinalId]
            : [FormatSelectionCriterion.Height, FormatSelectionCriterion.FrameRate, FormatSelectionCriterion.Width,
                FormatSelectionCriterion.VideoCodecPreference, FormatSelectionCriterion.TransportPreference,
                FormatSelectionCriterion.VideoBitrateEstimate, FormatSelectionCriterion.EmbeddedAudioPresence, FormatSelectionCriterion.OrdinalId,
                FormatSelectionCriterion.AudioCodecPreference, FormatSelectionCriterion.TransportPreference,
                FormatSelectionCriterion.AudioBitrateEstimate, FormatSelectionCriterion.OrdinalId];
        return FormatSelection.Create(info, quality, selected, audio, quality.Kind == QualityKind.Best
            ? FormatSelectionReason.RequestedBest : FormatSelectionReason.RequestedResolution, warnings, criteria);
    }

    private static int CompareVideo(VideoFormat left, VideoFormat right)
    {
        int comparison = Nullable.Compare(left.Height, right.Height);
        if (comparison != 0) return comparison;
        comparison = CompareRate(left.FrameRate, right.FrameRate);
        if (comparison != 0) return comparison;
        comparison = Nullable.Compare(left.Width, right.Width);
        if (comparison != 0) return comparison;
        comparison = VideoCodecRank(left.VideoCodec).CompareTo(VideoCodecRank(right.VideoCodec));
        if (comparison != 0) return comparison;
        comparison = TransportRank(left.Transport).CompareTo(TransportRank(right.Transport));
        if (comparison != 0) return comparison;
        // Bitrate is an estimate and is only compared inside the same declared codec family.
        if (VideoCodecRank(left.VideoCodec) >= 2)
        {
            comparison = Nullable.Compare(left.VideoBitrateKbps, right.VideoBitrateKbps);
            if (comparison != 0) return comparison;
        }
        return AudioPresenceRank(left.AudioStream).CompareTo(AudioPresenceRank(right.AudioStream));
    }
    private static int CompareAudio(VideoFormat left, VideoFormat right)
    {
        int comparison = AudioCodecRank(left.AudioCodec).CompareTo(AudioCodecRank(right.AudioCodec));
        if (comparison != 0) return comparison;
        comparison = TransportRank(left.Transport).CompareTo(TransportRank(right.Transport));
        if (comparison != 0) return comparison;
        return AudioCodecRank(left.AudioCodec) >= 2
            ? Nullable.Compare(left.AudioBitrateKbps, right.AudioBitrateKbps) : 0;
    }
    private static int CompareRate(FrameRate? left, FrameRate? right) => left is null ? right is null ? 0 : -1 :
        right is null ? 1 : ((BigInteger)left.Numerator * right.Denominator).CompareTo((BigInteger)right.Numerator * left.Denominator);
    private static int AudioPresenceRank(StreamPresence value) => value switch { StreamPresence.Present => 2, StreamPresence.Absent => 1, _ => 0 };
    private static int TransportRank(FormatTransport value) => value switch { FormatTransport.Https => 2, FormatTransport.Hls => 1, _ => 0 };
    private static int VideoCodecRank(string? codec) => VideoCodecFamily(codec) switch { "h264" => 4, "vp9" => 3, "av1" => 2, null => 0, _ => 1 };
    private static int AudioCodecRank(string? codec) => AudioCodecFamily(codec) switch { "aac" => 3, "opus" => 2, null => 0, _ => 1 };
    private static string? VideoCodecFamily(string? codec) => codec switch
    {
        "h264" or "avc1" => "h264", "vp9" or "vp09" => "vp9", "av1" or "av01" => "av1",
        _ when codec?.StartsWith("avc1.", StringComparison.Ordinal) == true => "h264",
        _ when codec?.StartsWith("vp09.", StringComparison.Ordinal) == true => "vp9",
        _ when codec?.StartsWith("av01.", StringComparison.Ordinal) == true => "av1", _ => codec
    };
    private static string? AudioCodecFamily(string? codec) => codec switch
    {
        "aac" => "aac", _ when codec?.StartsWith("mp4a.40.", StringComparison.Ordinal) == true => "aac", _ => codec
    };
    private static OperationResult<FormatSelection> Failed(OperationErrorCode code, OperationErrorCategory category, string message, string? field = null) =>
        OperationResult<FormatSelection>.Failed(new(code, category, message, field));
}
