using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Cli;

public sealed class CliApplication(IToolExecutionDiagnostics diagnostics, IVideoInfoService metadata,
    IClipJobService jobs, IOutputPathValidator outputPaths, ICliConsole console, Action<JobProgress>? stageLog = null)
{
    public async Task<int> RunAsync(CliCommand command, CancellationToken token)
    {
        try
        {
            if (command.Name == "diagnose")
            {
                var report = await diagnostics.DiagnoseAsync(new(ToolDiagnosticCommand.Diagnose), token).ConfigureAwait(false);
                if (report.Status != OperationStatus.Succeeded) return Result(report);
                foreach (var tool in report.Data!.Tools)
                {
                    console.Out($"{tool.Tool}: {ConsoleInteraction.Safe(tool.Location.SelectedPath)} | 실행 {tool.ExecutionStatus} | 버전 {tool.VersionStatus}: {ConsoleInteraction.Safe(tool.Version)}");
                    if (tool.Error is not null) ShowError(tool.Error);
                }
                return report.Data.AllRequestedChecksPassed ? 0 : 1;
            }
            if (command.Name == "clip")
            {
                var path = outputPaths.Validate(command.Output!);
                if (path.Status != OperationStatus.Succeeded) return Result(path);
            }
            console.Error("[Metadata] 조회 중\n");
            var info = await metadata.GetVideoInfoAsync(command.Url!, token).ConfigureAwait(false);
            if (info.Status != OperationStatus.Succeeded) return Result(info);
            if (command.Name == "info") { ShowInfo(info.Data!); return 0; }
            var request = new ClipRequest(command.Url!, command.Start!, command.End!, command.Output!, command.Quality, command.Mode);
            var prepared = await jobs.PrepareAsync(request, info.Data!, token).ConfigureAwait(false);
            if (prepared.Status != OperationStatus.Succeeded) return Result(prepared);
            ShowPrepared(info.Data!, prepared.Data!, command);
            if (!command.Yes)
            {
                if (console.InputRedirected)
                {
                    console.Error("비대화형 실행에는 --yes가 필요합니다.\n");
                    return 2;
                }
                if (!await ConsoleInteraction.ConfirmAsync(console, token).ConfigureAwait(false)) { console.Error("취소했습니다.\n"); return 130; }
            }
            token.ThrowIfCancellationRequested();
            var progress = new ConsoleProgress(console, stageLog);
            ClipJobResult result;
            try { result = await jobs.ExecuteAsync(prepared.Data!, progress, token).ConfigureAwait(false); }
            finally { progress.Finish(); }
            ShowAuxiliary(result.Warnings, result.RemainingTemporaryPaths);
            if (result.Status == OperationStatus.Cancelled) { console.Error("취소 및 정리 처리가 끝났습니다.\n"); return 130; }
            if (result.Status == OperationStatus.Failed) return ShowError(result.Error!);
            console.Out($"완료: {ConsoleInteraction.Safe(result.OutputPath)}");
            if (result.Presentation is { } presentation)
            {
                console.Out($"요청 범위: {Range(presentation.Requested)}");
                console.Out($"실제 영상 표시 범위: {Range(presentation.Video)}");
                console.Out($"실제 음성 표시 범위: {(presentation.Audio is null ? "없음" : Range(presentation.Audio))}");
                console.Out($"원본 전체 끝 검증: {presentation.SourceEndVerified}; 음성 zero 삽입 {presentation.InsertedAudioZeroSamples}, 중복 제거 {presentation.DiscardedAudioOverlapSamples}, AAC skip {presentation.AudioPrimingSkippedSamples}, tail {presentation.AudioDecodedTailPaddingSamples} samples");
            }
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { console.Error("취소했습니다.\n"); return 130; }
    }

    private int Result<T>(OperationResult<T> result) where T : class
    {
        ShowAuxiliary(result.Warnings, result.RemainingTemporaryPaths);
        if (result.Status == OperationStatus.Cancelled) { console.Error("취소했습니다.\n"); return 130; }
        return ShowError(result.Error!);
    }
    private int ShowError(OperationError error)
    {
        console.Error($"오류 [{error.Code}/{error.Category}] {ConsoleInteraction.Safe(error.Message)} ({ConsoleInteraction.Safe(error.Field)})\n");
        return error.Category == OperationErrorCategory.Input ? 2 : 1;
    }
    private void ShowAuxiliary(IEnumerable<string> warnings, IEnumerable<string> remaining)
    {
        foreach (string warning in warnings) console.Error($"주의: {ConsoleInteraction.Safe(warning)}\n");
        foreach (string path in remaining) console.Error($"잔존 임시 경로: {ConsoleInteraction.Safe(path)}\n");
    }
    private void ShowInfo(VideoInfo info)
    {
        console.Out($"제목: {ConsoleInteraction.Safe(info.Title)}");
        console.Out($"영상: {info.Source.Platform} / {info.Source.Id} / {info.State}");
        console.Out($"업로더: {ConsoleInteraction.Safe(info.Uploader)}; 길이: {info.Duration?.ToString() ?? "미확인"}");
        var heights = info.Formats.Where(f => f.VideoStream != StreamPresence.Absent && f.Height > 0)
            .Select(f => f.Height!.Value).Distinct().OrderDescending().Where(h =>
            { QualityOption.TryCreateResolution(h, out var q, out _); return new FormatSelector().Select(info, q!).Status == OperationStatus.Succeeded; });
        console.Out($"선택 화질: best {string.Join(' ', heights.Select(h => $"{h}p"))}");
        foreach (var format in info.Formats)
            console.Out($"포맷 {ConsoleInteraction.Safe(format.Id)}: {format.Width?.ToString() ?? "?"}x{format.Height?.ToString() ?? "?"}, FPS {format.FrameRate?.ToString() ?? "미확인"}, video {ConsoleInteraction.Safe(format.VideoCodec)}, audio {ConsoleInteraction.Safe(format.AudioCodec)} ({format.AudioStream})");
        foreach (var issue in info.Issues) console.Error($"메타데이터 미확인/제외: {ConsoleInteraction.Safe(issue.Field)} ({issue.Reason})\n");
    }
    private void ShowPrepared(VideoInfo info, PreparedClip prepared, CliCommand command)
    {
        console.Out($"제목: {ConsoleInteraction.Safe(prepared.Title)}");
        console.Out($"영상: {prepared.Selection.Source.Platform} / {prepared.Selection.Source.Id}; 업로더: {ConsoleInteraction.Safe(prepared.Uploader)}; 길이: {info.Duration}");
        var selected = prepared.Selection.Selection;
        var video = selected.VideoFormat;
        var audio = selected.AudioFormat ?? video;
        console.Out($"선택: {video.Height?.ToString() ?? "미확인"}p / FPS {video.FrameRate?.ToString() ?? "미확인"} / video {ConsoleInteraction.Safe(video.VideoCodec)} / audio {ConsoleInteraction.Safe(audio.AudioCodec)} ({audio.AudioStream}); 포맷 {ConsoleInteraction.Safe(video.Id)} + {ConsoleInteraction.Safe(selected.AudioFormat?.Id)}");
        console.Out($"IN {command.Start}, OUT {command.End}, 모드 {prepared.Selection.Mode}; 출력 {ConsoleInteraction.Safe(prepared.FinalPath)}");
        console.Out(prepared.Range is { } range ? $"요청 길이: {Fraction(Seconds(range.End) - Seconds(range.Start))} 초" : "요청 길이: FPS 검사 후 확인");
        console.Out($"획득 상한: {prepared.Budget.MaximumBytes} bytes / {Fraction(prepared.Budget.MaximumPresentationSpan)} 초 (원본 시작 참조 포함)");
        ShowAuxiliary(prepared.Warnings.Concat(selected.Warnings.Select(w => w.ToString())), []);
        foreach (string pending in prepared.PendingChecks) console.Error($"미확인 검사: {ConsoleInteraction.Safe(pending)}\n");
    }
    private static string Range(PresentationRange range) => $"[{Fraction(range.Start)}, {Fraction(range.End)}) 초";
    private static string Fraction(SignedMediaTime time) => time.Denominator.IsOne ? time.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture) :
        $"{time.Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{time.Denominator.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    private static SignedMediaTime Seconds(Timecode time)
    {
        var input = time.Input;
        var whole = (System.Numerics.BigInteger)input.Hours * 3600 + input.Minutes * 60 + input.Seconds;
        SignedMediaTime.TryCreate(whole * time.FrameRate.Numerator + (System.Numerics.BigInteger)input.Frames * time.FrameRate.Denominator,
            time.FrameRate.Numerator, out var result, out _);
        return result!;
    }
}
