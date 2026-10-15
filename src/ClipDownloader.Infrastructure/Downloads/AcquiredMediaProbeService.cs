using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed class AcquiredMediaProbeService(IMediaProbe probe)
{
    public async Task<OperationResult<AcquiredMediaInspection>> InspectAsync(AcquiredMedia acquisition, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (token.IsCancellationRequested) return OperationResult<AcquiredMediaInspection>.Cancelled();
        // Do not run tools with a fabricated expected hash or an unresolved source mapping.
        if (acquisition.Files.Any(f => f.Content is null) || acquisition.Mappings.Count == 0 ||
            acquisition.Mappings.Any(m => m.SourceOffset.State != MediaInspectionState.Verified || m.Coverage.State != MediaInspectionState.Verified))
            return Fail(OperationErrorCode.VerificationFailed, "AcquiredProbePrerequisite");
        var inspected = new List<AcquiredFileInspection>();
        foreach (var file in acquisition.Files)
        {
            if (token.IsCancellationRequested) return OperationResult<AcquiredMediaInspection>.Cancelled();
            var mappings = acquisition.Mappings.Where(m => m.SourceOffset.Scope.Stream.FileId == file.Id).ToArray();
            if (mappings.Length == 0) return Fail(OperationErrorCode.VerificationFailed, "AcquiredProbeStreams");
            var start = mappings.Select(m => m.Coverage.Value!.Start - m.SourceOffset.Value!).Min()!;
            var end = mappings.Select(m => m.Coverage.Value!.End - m.SourceOffset.Value!).Max()!;
            PresentationRange.TryCreate(start, end, out var range, out _);
            var request = new MediaProbeRequest(acquisition.AcquisitionId, file.Id, file.Path, file.Content!,
                MediaProbeDetail.FramesAndPackets, range!, inspectContainer: true);
            var result = await probe.ProbeAsync(request, token).ConfigureAwait(false);
            if (result.Status == MediaOperationStatus.Cancelled) return OperationResult<AcquiredMediaInspection>.Cancelled();
            if (result.Status == MediaOperationStatus.Failed) return Fail(result.Failure!);
            // A dependency may not silently substitute another request/snapshot.
            if (!ReferenceEquals(result.Data!.Request, request) || result.Data.Container is null)
                return Fail(OperationErrorCode.VerificationFailed, "AcquiredProbeIdentity");
            var native = new Mp4NativeObservationVerifier().Verify(result.Data.Observations, result.Data.Container);
            if (native.Status != MediaOperationStatus.Succeeded) return Fail(native.Failure!);
            SourceStartPrefixEvidence? beginning = null;
            bool needsBeginning = acquisition.Request.Selection.Mode == ClipMode.Accurate || mappings.Any(m =>
                (native.Data!.Streams.Single(s => s.Stream.Index == m.SourceOffset.Scope.Stream.StreamIndex).ObservedRange.Start +
                    m.SourceOffset.Value!).CompareTo(m.Coverage.Value!.Start) > 0);
            if (needsBeginning)
            {
                var prefix = await new SourceStartPrefixInspector().InspectAsync(acquisition, file, token).ConfigureAwait(false);
                if (prefix.Status == OperationStatus.Cancelled) return OperationResult<AcquiredMediaInspection>.Cancelled();
                if (prefix.Status != OperationStatus.Succeeded) return OperationResult<AcquiredMediaInspection>.Failed(prefix.Error!);
                beginning = prefix.Data;
            }
            inspected.Add(new(result.Data, native.Data!, beginning));
        }
        if (token.IsCancellationRequested) return OperationResult<AcquiredMediaInspection>.Cancelled();
        return AcquiredMediaInspection.Create(acquisition, inspected);
    }

    private static OperationResult<AcquiredMediaInspection> Fail(MediaFailure failure) => Fail(failure.Kind switch
    {
        MediaFailureKind.MissingTool => OperationErrorCode.MissingTool,
        MediaFailureKind.UnsupportedInput => OperationErrorCode.UnsupportedFormat,
        MediaFailureKind.ProcessFailed => OperationErrorCode.ProcessFailed,
        MediaFailureKind.TimedOut => OperationErrorCode.TimedOut,
        MediaFailureKind.TerminationUnconfirmed => OperationErrorCode.ProcessTerminationFailed,
        MediaFailureKind.OutputFailed => OperationErrorCode.OutputFailed,
        _ => OperationErrorCode.VerificationFailed
    }, failure.Field);
    private static OperationResult<AcquiredMediaInspection> Fail(OperationErrorCode code, string? field) =>
        OperationResult<AcquiredMediaInspection>.Failed(new(code, OperationErrorCategory.Execution,
            "획득 파일의 공유 로컬 검사 또는 native 연결을 완료하지 못했습니다.", field));
}
