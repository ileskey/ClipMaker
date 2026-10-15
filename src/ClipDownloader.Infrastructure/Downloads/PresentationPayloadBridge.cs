using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record PresentationPayloadEvidence(int PayloadCount, int TimestampAnchors, int MissingTimestamps, int FirstSourceSample = 0);

// Content and declared-clock correspondence only. Decoded picture equality remains a separate gate.
internal sealed class PresentationPayloadBridge
{
    internal Task<OperationResult<PresentationPayloadEvidence>> VerifyVideoAsync(VerifiedHlsNativeReference source,
        H264CarrierObservations carrier, CancellationToken token) => VerifyAsync(source, MediaStreamKind.Video, async (file, samples) =>
    {
        if (carrier.Pictures.Count != samples.Length) throw new InvalidDataException();
        var observed = source.Reference.Observations.Packets;
        var track = source.Reference.ContainerTiming.Tracks.Single();
        SignedMediaTime expected = SignedMediaTime.Zero;
        if (track.HasEditList) SignedMediaTime.TryCreate(track.Edits.Single().MediaTime, track.MediaTimescale, out expected!, out _);
        int anchors = 0, missing = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            byte[] bytes = await Payload(file, samples[i], token).ConfigureAwait(false);
            var identity = new H264PicturePayloadReader().Read(bytes, false);
            if (identity.Status != OperationStatus.Succeeded || identity.Data != carrier.Pictures[i].Payload) throw new InvalidDataException();
            if (carrier.Pictures[i].Pts is { } pts)
            {
                SignedMediaTime.TryCreate(pts, 90000, out var carrierTime, out _);
                SignedMediaTime.TryCreate(observed[i].Pts!.Value, track.MediaTimescale, out var movieTime, out _);
                if (carrierTime! - movieTime! != expected) throw new InvalidDataException();
                anchors++;
            }
            else missing++;
        }
        if (anchors < 2) throw new InvalidDataException();
        return new(samples.Length, anchors, missing);
    }, token);

    internal Task<OperationResult<PresentationPayloadEvidence>> VerifyAudioAsync(VerifiedHlsNativeReference source,
        PackedAacReference carrier, CancellationToken token) => VerifyAsync(source, MediaStreamKind.Audio, async (file, samples) =>
    {
        if (carrier.Frames.Count > samples.Length) throw new InvalidDataException();
        var track = source.Reference.ContainerTiming.Tracks.Single();
        // Native verification already proved the edit/packet skip/effective frame relation.
        // Packed AAC timestamps describe coded media, not the post-edit audible movie start.
        if (track.HasEditList && (track.Edits.Count != 1 || track.Edits[0].RateInteger != 1 || track.Edits[0].RateFraction != 0 ||
            track.Edits[0].MediaTime != source.Streams.Single().LeadingSkippedSamples || samples[0].PresentationTicks != 0))
            throw new InvalidDataException();
        var config = source.Streams.Single().Configuration;
        if (config.SampleRate is not { } rate || carrier.Frames.Any(f => f.SampleRate != rate || f.Channels != config.Channels || f.Samples != 1024))
            throw new InvalidDataException();
        long mediaTime = track.HasEditList ? track.Edits[0].MediaTime : 0;
        // The provider may omit packets that start before the audible movie start,
        // including the partially trimmed first audible packet. Prove the earliest
        // whole audible packet by native time, then every payload in the contiguous reference.
        int first = Array.FindIndex(samples, s => s.PresentationTicks - mediaTime >= 0);
        if (first < 0 || carrier.Frames.Count > samples.Length - first ||
            (samples[first].PresentationTicks - mediaTime) * 90000 / rate != carrier.TransportTicks)
            throw new InvalidDataException();
        for (int i = 0; i < carrier.Frames.Count; i++)
        {
            var sample = samples[first + i];
            if (sample.Duration != 1024 || sample.PresentationTicks != samples[first].PresentationTicks + i * 1024)
                throw new InvalidDataException();
            var bytes = await Payload(file, sample, token).ConfigureAwait(false);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != carrier.Frames[i].PayloadSha256) throw new InvalidDataException();
        }
        return new(carrier.Frames.Count, 1, 0, first);
    }, token);

    private static async Task<OperationResult<PresentationPayloadEvidence>> VerifyAsync(VerifiedHlsNativeReference source,
        MediaStreamKind kind, Func<FileStream, Mp4NativeSample[], Task<PresentationPayloadEvidence>> check, CancellationToken token)
    {
        try
        {
            if (source.Reference.Reference is not IndexedCollectedReference || source.Streams.Count != 1 || source.Streams[0].Stream.Kind != kind ||
                source.Reference.NativeSamples?.Data is not { } native) throw new InvalidDataException();
            var reference = source.Reference.Reference;
            await using var file = new FileStream(reference.File.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous);
            if (!await Matches(file, reference, token).ConfigureAwait(false)) throw new InvalidDataException();
            var samples = native.Samples.ToArray();
            if (samples.Length == 0) throw new InvalidDataException();
            var evidence = await check(file, samples).ConfigureAwait(false);
            if (!await Matches(file, reference, token).ConfigureAwait(false)) throw new InvalidDataException();
            token.ThrowIfCancellationRequested();
            return OperationResult<PresentationPayloadEvidence>.Succeeded(evidence);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<PresentationPayloadEvidence>.Cancelled(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        { return OperationResult<PresentationPayloadEvidence>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution,
            "선택 원본과 provider 참조의 payload 및 명시 timestamp 관계를 확인하지 못했습니다.", "PresentationPayloadBridge")); }
    }
    private static async Task<byte[]> Payload(FileStream file, Mp4NativeSample sample, CancellationToken token)
    {
        if (sample.Size is 0 or > 4 * 1024 * 1024 || sample.ByteOffset < 0 || sample.ByteOffset > file.Length - sample.Size) throw new InvalidDataException();
        byte[] bytes = new byte[(int)sample.Size]; file.Position = sample.ByteOffset;
        await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
    }
    private static async Task<bool> Matches(FileStream file, CollectedSourceReference reference, CancellationToken token)
    {
        if (file.Length != reference.Bytes) return false;
        file.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
