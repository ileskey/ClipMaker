using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Processing;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed class AudioPcmRenderService
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, MediaOwnedProcessingFile?> jobs = [];
    public MediaProcessingResources GetOwnedResources(Guid operationId)
    { lock (gate) return jobs.TryGetValue(operationId, out var file) && file is not null ? new([file], false, true) : new([], false, false); }

    public async Task<OperationResult<MediaAudioRenderInput>> RenderAsync(QualifiedClipPlan plan, Guid operationId,
        Guid fileId, string path, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (operationId == Guid.Empty || fileId == Guid.Empty || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Owned render identities/path are required.");
        if (token.IsCancellationRequested) return OperationResult<MediaAudioRenderInput>.Cancelled();
        lock (gate) { if (!jobs.TryAdd(operationId, null)) return Fail("PcmRenderOperationReuse"); }
        if (plan.Processing.Mode != MediaProcessingMode.Accurate || plan.Processing.AudioBoundary is not { } boundary ||
            plan.Processing.DecoderPreparation?.Value is not { } preparation ||
            !string.Equals(Path.GetDirectoryName(path), plan.Inspection.Acquisition.Request.WorkDirectory, StringComparison.OrdinalIgnoreCase))
            return Fail("PcmRenderPreparation");
        var audio = plan.Inspection.Streams.Single(s => s.Native.Stream.Kind == MediaStreamKind.Audio);
        var placement = new List<AudioPcmFramePlacement>(); long decodedFirst = 0;
        foreach (var frame in audio.Probe.Observations.Frames.Where(f => f.StreamIndex == audio.Native.Stream.Index))
        {
            NativeTimestamp.TryCreate(frame.Pts!.Value, audio.Native.Stream.TimeBase, out var timestamp, out _);
            var relative = timestamp!.PresentationTime - audio.Native.ObservedRange.Start;
            SignedMediaTime.TryCreate(1, preparation.SampleRate, out var sampleClock, out _);
            if (!relative.TryToNativeTicks(sampleClock!, NativeTimestampRounding.Exact, out long first, out _) || frame.SampleCount is not { } count)
                return Fail("PcmNativeLatticePlacement");
            placement.Add(new(decodedFirst, first, count)); decodedFirst = checked(decodedFirst + count);
        }
        AudioPcmReconstructionPlan reconstruction;
        try { reconstruction = new(boundary, preparation.Channels, preparation.Samples, placement); }
        catch (ArgumentException) { return Fail("PcmNativeSupport"); }
        OperationResult<MediaAudioRenderInput>? outcome = null;
        bool creatingOutput = false;
        FileStream? input = null, output = null;
        try
        {
            input = new(preparation.DecodedPcm.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (!await Matches(input, preparation.DecodedPcm.Content, token).ConfigureAwait(false)) return Fail("PcmRenderIdentityBefore");
            creatingOutput = true;
            output = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920, FileOptions.Asynchronous);
            creatingOutput = false;
            lock (gate) jobs[operationId] = new(fileId, path, null);
            var counts = await new AudioPcmReconstructor().ReconstructAsync(reconstruction, input, output, token).ConfigureAwait(false);
            await output.FlushAsync(token).ConfigureAwait(false);
            if (!await Matches(input, preparation.DecodedPcm.Content, token).ConfigureAwait(false)) return Fail("PcmRenderIdentityAfter");
            if (output.Length != checked(counts.WrittenSamples * preparation.Channels * 4)) return Fail("PcmRenderLength");
            output.Position = 0;
            var identity = new MediaFileContentIdentity(output.Length, Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false)));
            lock (gate) jobs[operationId] = new(fileId, path, identity);
            outcome = OperationResult<MediaAudioRenderInput>.Succeeded(new(boundary, preparation.DecodedPcm,
                new(fileId, path, identity), counts, preparation.Channels));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { outcome = OperationResult<MediaAudioRenderInput>.Cancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { outcome = creatingOutput && File.Exists(path) ? Fail("OutputConflict", OperationErrorCode.OutputConflict) :
            Fail("PcmRenderIo", output is null ? OperationErrorCode.VerificationFailed : OperationErrorCode.OutputFailed); }
        finally
        {
            foreach (var stream in new[] { input, output }.Where(s => s is not null))
                try { await stream!.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { outcome = Fail("PcmRenderClose", OperationErrorCode.OutputFailed); }
        }
        if (token.IsCancellationRequested && outcome?.Status == OperationStatus.Succeeded) return OperationResult<MediaAudioRenderInput>.Cancelled();
        return outcome ?? Fail("PcmRenderFailed");
    }

    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity content, CancellationToken token)
    {
        if (input.Length != content.Bytes) return false;
        input.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).Equals(content.Sha256, StringComparison.OrdinalIgnoreCase)
            && input.Length == content.Bytes;
    }
    private static OperationResult<MediaAudioRenderInput> Fail(string field, OperationErrorCode code = OperationErrorCode.VerificationFailed) => OperationResult<MediaAudioRenderInput>.Failed(
        new(code, OperationErrorCategory.Execution, "소유 PCM의 lossless sample-grid 재구성을 확인하지 못했습니다.", field));
}
