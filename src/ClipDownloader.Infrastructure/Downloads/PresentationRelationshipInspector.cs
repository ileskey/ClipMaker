using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record PresentationRelationshipEvidence(long BudgetUsed, PresentationPayloadEvidence VideoPayload,
    PresentationPayloadEvidence AudioPayload, PresentationPictureEvidence DecodedPictures);

internal sealed class PresentationRelationshipInspector(BoundedSourceHttpReader reader, PresentationVideoProbe videoProbe)
{
    internal async Task<OperationResult<PresentationRelationshipEvidence>> InspectAsync(SelectedSourceInspectionRequest request,
        SelectedSourceLocators locators, VerifiedHlsNativeReference video, VerifiedHlsNativeReference audio,
        long alreadyReadBytes, Action<OwnedMediaFile> register, CancellationToken token)
    {
        if (!request.Selection.Matches(locators.Selection) || video.Streams.Count != 1 || audio.Streams.Count != 1 ||
            video.Reference.Reference is not IndexedCollectedReference iv || audio.Reference.Reference is not IndexedCollectedReference ia ||
            iv.File.SourceFormatIds.Count != 1 || iv.File.SourceFormatIds[0] != locators.Video.Format.Id ||
            ia.File.SourceFormatIds.Count != 1 || ia.File.SourceFormatIds[0] != locators.Audio?.Format.Id ||
            iv.RequestedIndexRange != iv.ActualIndexRange || ia.RequestedIndexRange != ia.ActualIndexRange ||
            video.Streams[0].ObservedRange != iv.ActualIndexRange || audio.Streams[0].ObservedRange != ia.ActualIndexRange)
            return Fail();
        var declared = await new HlsPresentationReferenceResolver(reader).ResolveAsync(locators, request.Budget, alreadyReadBytes, token).ConfigureAwait(false);
        if (declared.Status != OperationStatus.Succeeded) return Translate(declared.Status, declared.Error);
        long used = declared.Data!.BudgetUsed;
        var collector = new AuxiliaryHlsReferenceCollector(reader);
        var files = new List<AuxiliaryHlsCollectedReference>();
        foreach (var locator in new[] { declared.Data.Video, declared.Data.Audio })
        {
            var collected = await collector.CollectFirstAsync(locator, request.WorkDirectory, request.Budget, used, token).ConfigureAwait(false);
            foreach (var remaining in collected.RemainingFiles) register(remaining);
            if (collected.Status != OperationStatus.Succeeded) return Translate(collected.Status, collected.Error);
            register(collected.Data!.File);
            files.Add(collected.Data);
            used = collected.Data.BudgetUsed;
        }
        var carrierVideo = await videoProbe.ObserveCarrierAsync(files[0], token).ConfigureAwait(false);
        if (carrierVideo.Status != OperationStatus.Succeeded) return Translate(carrierVideo.Status, carrierVideo.Error);
        var videoPayload = await new PresentationPayloadBridge().VerifyVideoAsync(video, carrierVideo.Data!, token).ConfigureAwait(false);
        if (videoPayload.Status != OperationStatus.Succeeded) return Translate(videoPayload.Status, videoPayload.Error);
        var audioBytes = await ReadOwnedAsync(files[1], token).ConfigureAwait(false);
        if (audioBytes.Status != OperationStatus.Succeeded) return Translate(audioBytes.Status, audioBytes.Error);
        var carrierAudio = new PackedAacReferenceReader().Read(audioBytes.Data!.Bytes);
        if (carrierAudio.Status != OperationStatus.Succeeded) return Translate(carrierAudio.Status, carrierAudio.Error);
        var audioPayload = await new PresentationPayloadBridge().VerifyAudioAsync(audio, carrierAudio.Data!, token).ConfigureAwait(false);
        if (audioPayload.Status != OperationStatus.Succeeded) return Translate(audioPayload.Status, audioPayload.Error);
        var moviePixels = await videoProbe.DecodeAsync(video.Reference.Reference, token).ConfigureAwait(false);
        if (moviePixels.Status != OperationStatus.Succeeded) return Translate(moviePixels.Status, moviePixels.Error);
        var carrierPixels = await videoProbe.DecodeAsync(files[0], token).ConfigureAwait(false);
        if (carrierPixels.Status != OperationStatus.Succeeded) return Translate(carrierPixels.Status, carrierPixels.Error);
        var pixels = new PresentationPictureVerifier().Verify(video, carrierVideo.Data!, moviePixels.Data!, carrierPixels.Data!);
        if (pixels.Status != OperationStatus.Succeeded) return Translate(pixels.Status, pixels.Error);
        token.ThrowIfCancellationRequested();
        return OperationResult<PresentationRelationshipEvidence>.Succeeded(new(used, videoPayload.Data!, audioPayload.Data!, pixels.Data!));
    }

    private sealed record OwnedBytes(byte[] Bytes);
    private static async Task<OperationResult<OwnedBytes>> ReadOwnedAsync(CollectedSourceReference reference, CancellationToken token)
    {
        try
        {
            if (reference.Bytes is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException();
            await using var file = new FileStream(reference.File.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (file.Length != reference.Bytes) throw new InvalidDataException();
            byte[] bytes = new byte[(int)reference.Bytes]; await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            return OperationResult<OwnedBytes>.Succeeded(new(bytes));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<OwnedBytes>.Cancelled(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { return OperationResult<OwnedBytes>.Failed(Error()); }
    }
    private static OperationError Error() => new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
        "초기 source와 provider presentation 참조의 관계를 확인하지 못했습니다.", "PresentationRelationship");
    private static OperationResult<PresentationRelationshipEvidence> Fail() => OperationResult<PresentationRelationshipEvidence>.Failed(Error());
    private static OperationResult<PresentationRelationshipEvidence> Translate(OperationStatus status, OperationError? error) =>
        status == OperationStatus.Cancelled ? OperationResult<PresentationRelationshipEvidence>.Cancelled() : OperationResult<PresentationRelationshipEvidence>.Failed(error!);
}
