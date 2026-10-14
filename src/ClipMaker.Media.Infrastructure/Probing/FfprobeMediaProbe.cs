using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipMaker.Media.Infrastructure.Probing;

// Initial shared strategy: local MP4 only. This returns raw demux observations;
// container/native interpretation and qualification remain separate gates.
public sealed class FfprobeMediaProbe(IToolLocator locator, ToolPathSettings settings, IProcessRunner runner) : IMediaProbe
{
    private const string Entries = "stream=index,id,codec_type,codec_name,profile,pix_fmt,sample_fmt,sample_rate,channels,extradata_size,time_base,start_pts,r_frame_rate,avg_frame_rate:frame=stream_index,media_type,pts,duration,nb_samples,sample_fmt,sample_rate,channels:packet=stream_index,pts,dts,duration,flags,size,pos:packet_side_data=side_data_type,skip_samples,discard_padding,skip_reason,discard_reason";

    public async Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string stage = "FileOpen";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var input = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            stage = "ByteIdentityBefore";
            if (!await Matches(input, request.Content, cancellationToken).ConfigureAwait(false))
                return Failed(MediaFailureKind.InvalidMedia, stage);
            Mp4FileTimingEvidence? container = null;
            if (request.InspectContainer)
            {
                stage = "ContainerRead";
                var timing = await new LocalMp4TimingReader().ReadAsync(input, cancellationToken,
                    request.Purpose == MediaProbePurpose.Output).ConfigureAwait(false);
                if (timing.Status == MediaOperationStatus.Cancelled) return MediaOperationResult<MediaProbeResult>.Cancelled();
                if (timing.Status != MediaOperationStatus.Succeeded) return MediaOperationResult<MediaProbeResult>.Failed(timing.Failure!);
                container = timing.Data!;
            }
            var location = locator.Locate(new("ffprobe.exe", settings.GetPath("ffprobe.exe")), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (location.Status != ToolLocationStatus.Found) return Failed(MediaFailureKind.MissingTool, "Ffprobe");
            stage = "ProbeExecution";
            string directory = Path.GetDirectoryName(request.Path)!;
            var version = await runner.RunAsync(new(location.SelectedPath!, ["-version"], directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(15)), null, cancellationToken).ConfigureAwait(false);
            var failure = Check(version);
            if (failure is not null) return failure;
            cancellationToken.ThrowIfCancellationRequested();
            string text = new UTF8Encoding(false, true).GetString(version.StandardOutput.Bytes.Span);
            var match = Regex.Match(text, @"\Affprobe version ([A-Za-z0-9][A-Za-z0-9._+\-]{0,127})(?:\s|$)", RegexOptions.CultureInvariant);
            if (!match.Success) return Failed(MediaFailureKind.ProcessFailed, "ProbeVersion");
            var arguments = new List<string> { "-v", "error", "-protocol_whitelist", "file", "-format_whitelist", "mov", "-show_streams" };
            if (request.Detail == MediaProbeDetail.FramesAndPackets) arguments.AddRange(["-show_frames", "-show_packets"]);
            arguments.AddRange(["-show_entries", Entries, "-of", "json", "-i", request.Path]);
            var result = await runner.RunAsync(new(location.SelectedPath!, arguments, directory,
                ProcessOutputMode.Full, ProcessOutputMode.Tail, TimeSpan.FromSeconds(60)), null, cancellationToken).ConfigureAwait(false);
            failure = Check(result);
            if (failure is not null) return failure;
            if (result.StandardError.TotalBytes != 0) return Failed(MediaFailureKind.InvalidMedia, "DecoderReportedError");
            cancellationToken.ThrowIfCancellationRequested();
            stage = "ByteIdentityAfter";
            if (!await Matches(input, request.Content, cancellationToken).ConfigureAwait(false))
                return Failed(MediaFailureKind.InvalidMedia, stage);
            var parsed = new FfprobeObservationParser().Parse(result.StandardOutput.Bytes, request.Detail == MediaProbeDetail.FramesAndPackets);
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed.Status != MediaOperationStatus.Succeeded) return MediaOperationResult<MediaProbeResult>.Failed(parsed.Failure!);
            if (request.Detail == MediaProbeDetail.FramesAndPackets && (!parsed.Data!.FramesObserved || !parsed.Data.PacketsObserved))
                return Failed(MediaFailureKind.InvalidMedia, "IncompleteObservation");
            if (container is not null)
            {
                var streams = parsed.Data!.Streams;
                var tracks = container.Initialization.Tracks;
                if (streams.Count != tracks.Count || streams.Any(s => s.TrackId is null) ||
                    streams.Select(s => s.TrackId).Distinct().Count() != streams.Count)
                    return Failed(MediaFailureKind.InvalidMedia, "ContainerTrackIdentity");
                foreach (var stream in streams)
                {
                    var track = tracks.SingleOrDefault(t => t.TrackId == stream.TrackId);
                    if (track is null || stream.TimeBase.Numerator != 1 || stream.TimeBase.Denominator != track.MediaTimescale)
                        return Failed(MediaFailureKind.InvalidMedia, "ContainerTrackClock");
                }
            }
            return MediaOperationResult<MediaProbeResult>.Succeeded(new(request, match.Groups[1].Value, parsed.Data!, container));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return MediaOperationResult<MediaProbeResult>.Cancelled(); }
        catch (DecoderFallbackException) { return Failed(MediaFailureKind.ProcessFailed, "ProbeVersion"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Failed(stage == "ProbeExecution" ? MediaFailureKind.ProcessFailed : MediaFailureKind.InvalidMedia, stage); }
    }

    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity content, CancellationToken token)
    {
        if (input.Length != content.Bytes) return false;
        input.Position = 0;
        var digest = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return input.Length == content.Bytes && Convert.ToHexString(digest).Equals(content.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaOperationResult<MediaProbeResult>? Check(ProcessResult result)
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed || result.DescendantExitConfirmed != true || !result.StreamsDrained)
            return Failed(MediaFailureKind.TerminationUnconfirmed, "ProbeTermination");
        if (result.Status == ProcessStatus.Cancelled) return MediaOperationResult<MediaProbeResult>.Cancelled();
        if (result.Status == ProcessStatus.TimedOut) return Failed(MediaFailureKind.TimedOut, "ProbeTimeout");
        if (result.StandardOutput.LimitExceeded || result.StandardOutput.Bytes.Length > FfprobeObservationParser.MaximumBytes)
            return Failed(MediaFailureKind.InvalidMedia, "ProbeOutputLimit");
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0) return Failed(MediaFailureKind.ProcessFailed, "ProbeExecution");
        if (!result.StandardOutput.IsComplete || !result.StandardOutput.ReachedEndOfStream || result.StandardOutput.IsTruncated ||
            result.StandardOutput.TotalBytes != result.StandardOutput.Bytes.Length)
            return Failed(MediaFailureKind.InvalidMedia, "ProbeOutputIncomplete");
        return null;
    }

    private static MediaOperationResult<MediaProbeResult> Failed(MediaFailureKind kind, string field) =>
        MediaOperationResult<MediaProbeResult>.Failed(new(kind, "로컬 미디어 검사 또는 검사 결과의 완전성을 확인하지 못했습니다.", field));
}
