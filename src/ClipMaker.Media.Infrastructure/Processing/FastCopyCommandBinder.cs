using System.Globalization;
using System.Numerics;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Infrastructure.Processing;

internal sealed class FastCopyCommand
{
    internal IReadOnlyList<string> Arguments { get; }
    internal FastCopyCommand(IEnumerable<string> arguments) => Arguments = Array.AsReadOnly(arguments.ToArray());
}

internal sealed class FastCopyCommandBinder
{
    private static readonly BigInteger ExactDoubleIntegerLimit = (BigInteger.One << 53) - 1;
    internal MediaOperationResult<FastCopyCommand> Bind(MediaProcessingRequest request)
    {
        var plan = request.Plan;
        if (plan.Mode != MediaProcessingMode.Fast) return Fail("AccurateStrategyUnavailable");
        if (request.AudioRender is not null) return Fail("UnexpectedFastAudioRender");
        var video = plan.Streams.Where(s => s.Stream.Kind == MediaStreamKind.Video).ToArray();
        var audio = plan.Streams.Where(s => s.Stream.Kind == MediaStreamKind.Audio).ToArray();
        if (video.Length != 1 || video[0].Codec != "h264" || audio.Length > 1 || audio.Any(a => a.Codec != "aac" ||
            a.SampleRate is null || a.Channels is not (1 or 2))) return Fail("CopyStrategyUnsupported");
        // Current source strategies preserve a single local movie axis. Separate
        // input stop coordinates require another verified copy/mux strategy.
        if (audio.Any(a => a.Translation != video[0].Translation || a.InputRange.End != video[0].InputRange.End ||
            a.InputRange.Start.CompareTo(video[0].InputRange.Start) < 0)) return Fail("SplitCopyEndCoordinatesUnsupported");
        SignedMediaTime.TryCreate(1, plan.MovieTimescale, out var common, out _);
        var deltas = new Dictionary<MediaStreamReference, long>();
        foreach (var stream in plan.Streams)
        {
            BigInteger numerator = stream.InputTimeBase.Numerator * plan.MovieTimescale;
            var factor = BigInteger.DivRem(numerator, stream.InputTimeBase.Denominator, out var remainder);
            if (!remainder.IsZero || !stream.Translation.TryToNativeTicks(common!, NativeTimestampRounding.Exact, out long delta, out _) ||
                BigInteger.Abs(delta) > ExactDoubleIntegerLimit) return Fail("NativeClockExpressionUnsupported");
            foreach (var ticks in new[] { stream.MinimumInputTimestamp, stream.MaximumInputTimestamp })
            {
                var scaled = ticks * factor;
                if (BigInteger.Abs(scaled) > ExactDoubleIntegerLimit || BigInteger.Abs(scaled + delta) > ExactDoubleIntegerLimit)
                    return Fail("NativeClockExpressionUnsupported");
            }
            if (stream.Stream.Kind == MediaStreamKind.Audio && (!stream.Translation.TryToNativeTicks(stream.InputTimeBase,
                NativeTimestampRounding.Exact, out _, out _) || stream.InputTimeBase != Time(1, stream.SampleRate!.Value)))
                return Fail("FastAudioGridUnsupported");
            deltas.Add(stream.Stream, delta);
        }
        if (plan.RequiredVideoFrames.Any(f => f.InputTimeBase != video[0].InputTimeBase ||
            Time(f.InputPts * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator) + video[0].Translation != f.OutputPts ||
            Time(f.Duration * f.InputTimeBase.Numerator, f.InputTimeBase.Denominator) != f.OutputDuration))
            return Fail("VideoExpectationContradicted");
        // Tool seek is rounded down so it never passes the verified IDR. Keep
        // the native range/translation exact; output packet/picture verification
        // must prove the actual result. OUT is likewise only a tool coordinate.
        var micros = Time(1, 1_000_000);
        if (video[0].InputRange.Start.Numerator < 0 || !video[0].InputRange.Start.TryToNativeTicks(micros,
            NativeTimestampRounding.Floor, out long seek, out _) || !video[0].InputRange.End.TryToNativeTicks(micros,
            NativeTimestampRounding.Ceiling, out long stop, out _) || stop <= seek) return Fail("CopySeekClockUnsupported");
        if (plan.Inputs.Any(f => string.Equals(f.Path, request.StagingPath, StringComparison.OrdinalIgnoreCase)))
            return Fail("StagingInputConflict");
        var args = new List<string> { "-v", "error", "-xerror", "-nostdin", "-y", "-copyts" };
        foreach (var input in plan.Inputs)
            args.AddRange(["-protocol_whitelist", "file", "-format_whitelist", "mov", "-seek_timestamp", "1", "-ss", Seconds(seek), "-i", input.Path]);
        foreach (var stream in video.Concat(audio))
        {
            int index = plan.Inputs.ToList().FindIndex(f => f.Id == stream.Stream.FileId);
            args.AddRange(["-map", $"{index}:{stream.Stream.StreamIndex}"]);
        }
        args.AddRange(["-map_metadata", "-1", "-to", Seconds(stop), "-c", "copy"]);
        foreach (var stream in video.Concat(audio))
        {
            string kind = stream.Stream.Kind == MediaStreamKind.Video ? "v" : "a";
            string shift = deltas[stream.Stream].ToString(CultureInfo.InvariantCulture);
            args.AddRange([$"-bsf:{kind}:0", $"setts=time_base=1/{plan.MovieTimescale}:prescale=1:pts=PTS+({shift}):dts=DTS+({shift})"]);
        }
        args.AddRange(["-avoid_negative_ts", "disabled", "-movie_timescale", plan.MovieTimescale.ToString(CultureInfo.InvariantCulture),
            "-video_track_timescale", plan.MovieTimescale.ToString(CultureInfo.InvariantCulture), "-f", "mp4", request.StagingPath]);
        return MediaOperationResult<FastCopyCommand>.Succeeded(new(args));
    }
    private static SignedMediaTime Time(BigInteger n, BigInteger d)
    { SignedMediaTime.TryCreate(n, d, out var time, out _); return time!; }
    private static string Seconds(long microseconds) => (microseconds / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);
    private static MediaOperationResult<FastCopyCommand> Fail(string field) => MediaOperationResult<FastCopyCommand>.Failed(
        new(MediaFailureKind.UnsupportedInput, "Copy 명령의 native 시간·숫자·stream 표현 조건을 지원하지 않습니다.", field));
}
