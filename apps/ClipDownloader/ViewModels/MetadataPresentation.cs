using System.Globalization;
using ClipDownloader.Application.Models;

namespace ClipDownloader.ViewModels;

public sealed record MetadataPresentation(string Title, string Uploader, string Duration, string FrameRate,
    string Resolutions, string Issues)
{
    public static MetadataPresentation Empty { get; } = new("영상 정보 없음", "영상 정보 없음", "영상 정보 없음", "선택 포맷 없음", "영상 정보 없음", "");

    public static int[] Heights(VideoInfo info) => info.Formats
        .Where(f => f.VideoStream != StreamPresence.Absent && f.Height > 0)
        .Select(f => f.Height!.Value).Distinct().OrderDescending().ToArray();

    public static IReadOnlyList<QualityOption> QualityOptions(VideoInfo info) => new[] { QualityOption.Best }
        .Concat(Heights(info).Select(h => { QualityOption.TryCreateResolution(h, out var q, out _); return q!; })).ToArray();

    public static MetadataPresentation Create(VideoInfo info, FormatSelection? selection)
    {
        var rate = selection?.VideoFormat.FrameRate;
        string fps = selection is null ? "선택 포맷 없음" : rate is null ? "FPS 미확인 · 실행 중 검사 대기" :
            string.Create(CultureInfo.InvariantCulture, $"{rate.Numerator}/{rate.Denominator} fps (약 {(decimal)rate.Numerator / rate.Denominator:0.000})");
        string duration = "길이 미확인";
        if (info.Duration is { Ticks: > 0 } d)
        {
            long seconds = d.Ticks / TimeSpan.TicksPerSecond;
            string fraction = (d.Ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
            duration = string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:D2}:{seconds / 60 % 60:D2}:{seconds % 60:D2}") +
                (fraction.Length == 0 ? "" : "." + fraction);
        }
        string resolutions = string.Join(" · ", Heights(info).Select(h => $"{h}p"));
        return new(string.IsNullOrWhiteSpace(info.Title) ? "제목 미확인" : info.Title,
            string.IsNullOrWhiteSpace(info.Uploader) ? "업로더 미확인" : info.Uploader, duration, fps,
            resolutions.Length == 0 ? "해상도 미확인" : resolutions,
            string.Join("\n", info.Issues.Select(i => $"{Field(i.Field)}: {Reason(i.Reason)}" +
                (i.FormatIndex is { } index ? $" (원본 format index {index})" : ""))));
    }

    private static string Field(string field) => field switch
    {
        "title" => "제목", "uploader" or "channel" => "업로더/채널", "duration" => "총 길이",
        "thumbnail" => "썸네일", "fps" => "FPS", "width" or "height" => "해상도",
        "formats" or "format_id" => "포맷", "vcodec" or "acodec" => "코덱", "upload_date" => "업로드 날짜",
        _ => "정보 항목"
    };
    private static string Reason(MetadataIssueReason reason) => reason switch
    {
        MetadataIssueReason.Missing => "없음", MetadataIssueReason.Rounded => "표현 가능한 단위로 반올림됨",
        MetadataIssueReason.InvalidType => "자료 형식 오류", MetadataIssueReason.InvalidValue => "값 오류",
        MetadataIssueReason.OutOfRange => "지원 범위 밖", MetadataIssueReason.Ambiguous => "중복/모호한 항목",
        MetadataIssueReason.DuplicateFormat => "중복 포맷", MetadataIssueReason.Unsupported => "지원하지 않음",
        MetadataIssueReason.UnsafeReference => "주소 정책에 따라 거절됨", _ => "추가 확인 필요"
    };
}
