using System.Globalization;
using System.Numerics;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Infrastructure.Processing;

internal sealed class AccurateEncodingCommandBinder
{
    private static readonly BigInteger Limit = (BigInteger.One << 53) - 1;
    internal MediaOperationResult<FastCopyCommand> Bind(MediaProcessingRequest request)
    {
        var plan = request.Plan;
        var videos = plan.Streams.Where(s => s.Stream.Kind == MediaStreamKind.Video).ToArray();
        var audios = plan.Streams.Where(s => s.Stream.Kind == MediaStreamKind.Audio).ToArray();
        if (plan.Mode != MediaProcessingMode.Accurate || videos.Length != 1 || videos[0].Codec != "h264" || audios.Length > 1)
            return Fail("AccurateStrategyUnsupported");
        var video = videos[0]; var input = plan.Inputs.Single(f => f.Id == video.Stream.FileId);
        SignedMediaTime.TryCreate(1, plan.MovieTimescale, out var clock, out _);
        if (!video.InputRange.Start.TryToNativeTicks(clock!, NativeTimestampRounding.Exact, out long first, out _) ||
            !video.InputRange.End.TryToNativeTicks(clock!, NativeTimestampRounding.Exact, out long end, out _) ||
            !video.Translation.TryToNativeTicks(clock!, NativeTimestampRounding.Exact, out long shift, out _) ||
            BigInteger.Abs(first) > Limit || BigInteger.Abs(end) > Limit || BigInteger.Abs(shift) > Limit)
            return Fail("AccurateNativeExpressionUnsupported");
        var factor = BigInteger.DivRem(video.InputTimeBase.Numerator * plan.MovieTimescale, video.InputTimeBase.Denominator, out var remainder);
        if (!remainder.IsZero || new[] { video.MinimumInputTimestamp, video.MaximumInputTimestamp }.Any(t =>
            BigInteger.Abs(t * factor) > Limit || BigInteger.Abs(t * factor + shift) > Limit)) return Fail("AccurateNativeExpressionUnsupported");
        if (plan.RequiredVideoFrames.Any(f => f.InputTimeBase != video.InputTimeBase ||
            Time(f.InputPts * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator).CompareTo(video.InputRange.Start) < 0 ||
            Time(f.InputPts * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator).CompareTo(video.InputRange.End) >= 0 ||
            Time(f.InputPts * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator) + video.Translation != f.OutputPts ||
            Time(f.Duration * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator) != f.OutputDuration)) return Fail("AccurateVideoExpectation");
        var args = new List<string> { "-v", "error", "-xerror", "-nostdin", "-y", "-copyts",
            "-protocol_whitelist", "file", "-format_whitelist", "mov", "-i", input.Path };
        var render = request.AudioRender;
        if (audios.Length == 1)
        {
            var audio = audios[0]; var preparation = plan.DecoderPreparation;
            if (audio.Codec != "aac" || audio.Channels is not (1 or 2) || audio.SampleRate is null || render is null ||
                plan.AudioBoundary is null || !ReferenceEquals(plan.AudioBoundary, render.Boundary) ||
                preparation?.State != MediaInspectionState.Verified || preparation.Value is null ||
                render.DecodedPcm != preparation.Value.DecodedPcm || preparation.Value.SampleRate != audio.SampleRate ||
                preparation.Value.Channels != audio.Channels) return Fail("AccurateAudioRenderUnverified");
            // Delayed first support requires an independently verified AAC edit
            // strategy. Do not reset Qa or invent silence outside source support.
            if (render.Boundary.SelectedFirst != render.Boundary.RequestedFirst) return Fail("AccurateDelayedAudioEditUnsupported");
            args.AddRange(["-protocol_whitelist", "file", "-format_whitelist", "f32le", "-f", "f32le",
                "-ar", Number(audio.SampleRate.Value), "-ac", Number(audio.Channels.Value), "-i", render.RenderPcm.Path]);
        }
        else if (render is not null || plan.AudioBoundary is not null) return Fail("UnexpectedAudioRender");
        if (plan.Inputs.Concat(render is null ? [] : new[] { render.DecodedPcm, render.RenderPcm }).Any(f =>
            string.Equals(f.Path, request.StagingPath, StringComparison.OrdinalIgnoreCase))) return Fail("StagingInputConflict");
        args.AddRange(["-map", $"0:{video.Stream.StreamIndex}", "-map_metadata", "-1", "-filter:v:0",
            $"settb=expr=1/{plan.MovieTimescale},trim=start_pts={Number(first)}:end_pts={Number(end)},setpts=PTS+({Number(shift)})",
            "-c:v:0", "libx264", "-preset:v:0", "medium", "-crf:v:0", "18", "-bf:v:0", "0", "-pix_fmt:v:0", "yuv420p",
            "-fps_mode:v:0", "passthrough", "-enc_time_base:v:0", $"1/{plan.MovieTimescale}"]);
        if (render is not null) args.AddRange(["-map", "1:a:0", "-c:a:0", "aac", "-b:a:0", "192k"]);
        else args.Add("-an");
        args.AddRange(["-avoid_negative_ts", "disabled", "-movie_timescale", Number(plan.MovieTimescale),
            "-video_track_timescale", Number(plan.MovieTimescale), "-f", "mp4", request.StagingPath]);
        return MediaOperationResult<FastCopyCommand>.Succeeded(new(args));
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static SignedMediaTime Time(BigInteger n, BigInteger d) { SignedMediaTime.TryCreate(n, d, out var value, out _); return value!; }
    private static MediaOperationResult<FastCopyCommand> Fail(string field) => MediaOperationResult<FastCopyCommand>.Failed(
        new(MediaFailureKind.UnsupportedInput, "Accurate encode의 준비·native 표현·audio edit 전략을 확인하지 못했습니다.", field));
}
