using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed class ClipRandomAccessInspector
{
    public async Task<OperationResult<MediaInspectionFact<MediaRandomAccessPoint>>> InspectAsync(
        AcquiredMediaInspection inspection, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        if (token.IsCancellationRequested) return OperationResult<MediaInspectionFact<MediaRandomAccessPoint>>.Cancelled();
        var origin = inspection.Acquisition.Request.Inspection.Timeline.Origin;
        if (origin.State != MediaInspectionState.Verified) return Fail("OriginUnverified");
        var videos = inspection.Streams.Where(s => s.Native.Stream.Kind == MediaStreamKind.Video).ToArray();
        if (videos.Length != 1 || videos[0].Native.Configuration.Codec != "h264") return Fail("RandomAccessUnsupported", true);
        var video = videos[0];
        var localIn = origin.Value! + SignedMediaTime.FromTimecode(inspection.Acquisition.Request.Range.Start) - video.Mapping.SourceOffset.Value!;
        var packet = video.Probe.Observations.Packets.Where(p => p.StreamIndex == video.Native.Stream.Index && p.Pts is not null &&
            p.Flags?.Contains('K') == true).Where(p => Time(p.Pts!.Value, video.Native.Stream.TimeBase).CompareTo(localIn) <= 0)
            .OrderByDescending(p => p.Pts).FirstOrDefault();
        if (packet?.Pts is not { } pts || packet.Position is not >= 0 || packet.Size is not > 0 || packet.Size > 4 * 1024 * 1024)
            return Fail("KeyframeUnverified");
        var frame = video.Probe.Observations.Frames.SingleOrDefault(f => f.StreamIndex == video.Native.Stream.Index && f.Pts == pts);
        if (frame?.Duration is not > 0) return Fail("KeyframeUnverified");
        try
        {
            var request = video.Probe.Request;
            await using var input = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (!await Matches(input, request.Content, token).ConfigureAwait(false)) return Fail("ByteIdentityBefore");
            if (packet.Position > input.Length - packet.Size) return Fail("RandomAccessByteRange");
            var bytes = new byte[packet.Size.Value]; input.Position = packet.Position.Value;
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            var picture = new H264PicturePayloadReader().Read(bytes, annexB: false);
            if (picture.Status != OperationStatus.Succeeded || !picture.Data!.AllSlicesIdr)
                return Fail("ClosedRandomAccessUnsupported", true);
            if (!await Matches(input, request.Content, token).ConfigureAwait(false)) return Fail("ByteIdentityAfter");
            token.ThrowIfCancellationRequested();
            NativeTimestamp.TryCreate(pts, video.Native.Stream.TimeBase, out var timestamp, out _);
            PresentationRange.TryCreate(timestamp!.PresentationTime,
                timestamp.PresentationTime + Time(frame.Duration.Value, video.Native.Stream.TimeBase), out var range, out _);
            var scope = new MediaObservationScope(inspection.Acquisition.AcquisitionId, video.Mapping.SourceOffset.Scope.Stream,
                MediaInspectionKind.Frames, MediaTimeBasis.Native, range!, ObservationCompleteness.Complete);
            return OperationResult<MediaInspectionFact<MediaRandomAccessPoint>>.Succeeded(
                MediaInspectionFact<MediaRandomAccessPoint>.Verified(new(timestamp, picture.Data.Sha256), scope));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<MediaInspectionFact<MediaRandomAccessPoint>>.Cancelled(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Fail("RandomAccessRead"); }
    }

    private static SignedMediaTime Time(long pts, SignedMediaTime clock)
    { SignedMediaTime.TryCreate((System.Numerics.BigInteger)pts * clock.Numerator, clock.Denominator, out var time, out _); return time!; }
    private static async Task<bool> Matches(FileStream input, MediaFileContentIdentity identity, CancellationToken token)
    {
        if (input.Length != identity.Bytes) return false;
        input.Position = 0;
        var hash = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return input.Length == identity.Bytes && Convert.ToHexString(hash).Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static OperationResult<MediaInspectionFact<MediaRandomAccessPoint>> Fail(string field, bool unsupported = false) =>
        OperationResult<MediaInspectionFact<MediaRandomAccessPoint>>.Failed(new(unsupported ? OperationErrorCode.UnsupportedFormat :
            OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "선행 H.264 random access 근거를 확인하지 못했습니다.", field));
}
