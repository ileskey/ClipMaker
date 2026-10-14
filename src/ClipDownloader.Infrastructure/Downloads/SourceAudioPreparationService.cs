using System.Numerics;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Processing;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed class SourceAudioPreparationService(FfmpegMediaProcessingService processor)
{
    // Source-start acquisition is bounded by the existing adapter budget. This
    // decodes only those captured bytes, never seeks or downloads a fallback.
    public async Task<OperationResult<MediaInspectionFact<AudioDecoderPreparation>>> PrepareAsync(
        AcquiredMediaInspection inspection, Guid operationId, Guid pcmFileId, string pcmPath, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (token.IsCancellationRequested) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Cancelled();
        var audio = inspection.Streams.Where(s => s.Native.Stream.Kind == MediaStreamKind.Audio).ToArray();
        if (inspection.Acquisition.Request.Selection.Mode != ClipMode.Accurate || audio.Length != 1)
            return Fail("SourceAudioPreparationStrategy");
        var stream = audio[0];
        var file = inspection.Acquisition.Files.Single(f => f.Id == stream.Probe.Request.FileId);
        var prefix = inspection.Files.Single(f => f.Probe.Request.FileId == file.Id).SourceStart;
        if (prefix is null || stream.Native.Configuration.Codec != "aac" || stream.Native.Stream.Codec.Profile != "LC" ||
            stream.Native.Stream.Codec.SampleRate is not { } rate || stream.Native.Stream.Codec.Channels is not (1 or 2) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(pcmPath)), inspection.Acquisition.Request.WorkDirectory, StringComparison.OrdinalIgnoreCase))
            return Fail("SourceAudioPreparationIdentity");
        var frames = stream.Probe.Observations.Frames.Where(f => f.StreamIndex == stream.Native.Stream.Index).ToArray();
        if (frames.Length == 0 || frames.Any(f => f.Pts is null || f.SampleCount != 1024 || f.SampleRate is { } frameRate && frameRate != rate ||
            f.Channels != stream.Native.Stream.Codec.Channels || f.SampleFormat != "fltp")) return Fail("SourceDecodedFrameLayout");
        var samples = frames.Aggregate(BigInteger.Zero, (sum, frame) => sum + frame.SampleCount!.Value);
        if (samples <= 0 || samples > long.MaxValue / (stream.Native.Stream.Codec.Channels.Value * 4)) return Fail("SourceDecodedSampleBounds");
        var beginning = await new SourceStartPrefixInspector().InspectAsync(inspection.Acquisition, file, token).ConfigureAwait(false);
        if (beginning.Status == OperationStatus.Cancelled) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Cancelled();
        if (beginning.Status != OperationStatus.Succeeded) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Failed(beginning.Error!);
        if (beginning.Data != prefix) return Fail("SourceStartPreparationChanged");
        var request = new MediaAudioDecodeRequest(operationId, pcmFileId, new(file.Id, file.Path, file.Content!),
            stream.Native.Stream.Index, rate, stream.Native.Stream.Codec.Channels.Value, (long)samples, pcmPath);
        var result = await processor.DecodeAudioAsync(request, token).ConfigureAwait(false);
        if (result.Status == MediaOperationStatus.Cancelled) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Cancelled();
        if (result.Status != MediaOperationStatus.Succeeded)
            return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Failed(new(
                result.Failure!.Kind == MediaFailureKind.TerminationUnconfirmed ? OperationErrorCode.ProcessTerminationFailed :
                result.Failure.Kind == MediaFailureKind.MissingTool ? OperationErrorCode.MissingTool :
                result.Failure.Kind == MediaFailureKind.TimedOut ? OperationErrorCode.TimedOut :
                result.Failure.Kind == MediaFailureKind.OutputFailed ? result.Failure.Field == "OutputConflict" ? OperationErrorCode.OutputConflict : OperationErrorCode.OutputFailed :
                result.Failure.Kind == MediaFailureKind.UnsupportedInput ? OperationErrorCode.UnsupportedFormat :
                result.Failure.Kind == MediaFailureKind.ProcessFailed ? OperationErrorCode.ProcessFailed : OperationErrorCode.VerificationFailed,
                OperationErrorCategory.Execution, result.Failure.Message, result.Failure.Field));
        beginning = await new SourceStartPrefixInspector().InspectAsync(inspection.Acquisition, file, token).ConfigureAwait(false);
        if (beginning.Status == OperationStatus.Cancelled) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Cancelled();
        if (beginning.Status != OperationStatus.Succeeded) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Failed(beginning.Error!);
        if (beginning.Data != prefix) return Fail("SourceStartPreparationChanged");
        var value = new AudioDecoderPreparation(prefix.SourceFirstFileId, prefix.SourceFirstContent,
            new(result.Data!.Pcm.Id, result.Data.Pcm.Path, result.Data.Pcm.Content!), rate, request.Channels, (long)samples);
        if (token.IsCancellationRequested) return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Cancelled();
        return OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Succeeded(
            MediaInspectionFact<AudioDecoderPreparation>.Verified(value, stream.Mapping.Coverage.Scope));
    }

    private static OperationResult<MediaInspectionFact<AudioDecoderPreparation>> Fail(string field) =>
        OperationResult<MediaInspectionFact<AudioDecoderPreparation>>.Failed(new(OperationErrorCode.VerificationFailed,
            OperationErrorCategory.Execution, "실제 source-start decode와 선택 원본의 준비 대응을 확인하지 못했습니다.", field));
}
