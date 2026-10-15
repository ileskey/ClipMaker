using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

public sealed record SourceStartPrefixEvidence(Guid SourceFirstFileId, MediaFileContentIdentity SourceFirstContent,
    Guid TargetFileId, MediaFileContentIdentity TargetContent, string PrefixSha256);
public sealed record AcquiredFileInspection(MediaProbeResult Probe, Mp4NativeObservation Native, SourceStartPrefixEvidence? SourceStart = null);
public sealed record AcquiredStreamInspection(SourceStreamMapping Mapping, MediaProbeResult Probe, Mp4NativeStreamObservation Native);

// Local probe/source mapping binding only; processing qualification is separate.
public sealed class AcquiredMediaInspection
{
    public AcquiredMedia Acquisition { get; }
    public IReadOnlyList<AcquiredFileInspection> Files { get; }
    public IReadOnlyList<AcquiredStreamInspection> Streams { get; }
    private AcquiredMediaInspection(AcquiredMedia acquisition, AcquiredFileInspection[] files, AcquiredStreamInspection[] streams)
    { Acquisition = acquisition; Files = Array.AsReadOnly(files); Streams = Array.AsReadOnly(streams); }

    public static OperationResult<AcquiredMediaInspection> Create(AcquiredMedia acquisition, IEnumerable<AcquiredFileInspection> inspections)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentNullException.ThrowIfNull(inspections);
        var files = inspections.ToArray();
        if (files.Any(f => f is null || f.Probe is null || f.Native is null) || files.Length != acquisition.Files.Count ||
            files.Select(f => f.Probe.Request.FileId).Distinct().Count() != files.Length)
            return Fail("AcquiredProbeFiles");
        foreach (var file in files)
        {
            var owned = acquisition.Files.SingleOrDefault(f => f.Id == file.Probe.Request.FileId);
            var request = file.Probe.Request;
            if (owned is null || owned.Content is null || request.Content != owned.Content || request.SnapshotId != acquisition.AcquisitionId ||
                !string.Equals(request.Path, owned.Path, StringComparison.OrdinalIgnoreCase) || !request.InspectContainer ||
                request.Detail != MediaProbeDetail.FramesAndPackets || request.Purpose != MediaProbePurpose.Source || file.Probe.Container is null)
                return Fail("AcquiredProbeIdentity");
            if (file.Native.Streams.Count != file.Probe.Observations.Streams.Count ||
                file.Native.Streams.Any(s => !file.Probe.Observations.Streams.Contains(s.Stream)) ||
                file.Native.Streams.Select(s => s.Stream.Index).Distinct().Count() != file.Native.Streams.Count)
                return Fail("AcquiredNativeIdentity");
            if (file.SourceStart is { } beginning)
            {
                var first = acquisition.Request.Inspection.Files.SingleOrDefault(f => f.Id == beginning.SourceFirstFileId);
                if (first?.Content is null || first.Content != beginning.SourceFirstContent ||
                    beginning.TargetFileId != owned.Id || beginning.TargetContent != owned.Content ||
                    !first.SourceFormatIds.SequenceEqual(owned.SourceFormatIds) || beginning.SourceFirstContent.Bytes > owned.Content.Bytes ||
                    !string.Equals(beginning.PrefixSha256, first.Content.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Fail("SourceStartPrefixIdentity");
            }
            var expected = acquisition.Mappings.Where(m => m.SourceOffset.Scope.Stream.FileId == owned.Id)
                .Select(m => m.SourceOffset.Scope.Stream).ToArray();
            if (expected.Length != file.Native.Streams.Count || file.Native.Streams.Any(s =>
                !expected.Contains(new MediaStreamReference(owned.Id, s.Stream.Index, s.Stream.Kind))))
                return Fail("AcquiredProbeStreams");
            if (file.Probe.Observations.Streams.Any(s => s.Kind == MediaStreamKind.Video && s.DeclaredRate is { } rate &&
                rate != acquisition.Request.Range.FrameRate))
                return OperationResult<AcquiredMediaInspection>.Failed(new(OperationErrorCode.InvalidMetadata,
                    OperationErrorCategory.Execution, "선택 FPS와 획득 파일의 관찰 FPS가 달라졌습니다.", "SelectedFrameRateChanged"));
        }
        var streams = new List<AcquiredStreamInspection>();
        foreach (var mapping in acquisition.Mappings)
        {
            if (mapping.SourceOffset.State != MediaInspectionState.Verified || mapping.Coverage.State != MediaInspectionState.Verified)
                return Fail("AcquiredMappingUnverified");
            var file = files.Single(f => f.Probe.Request.FileId == mapping.SourceOffset.Scope.Stream.FileId);
            var native = file.Native.Streams.Single(s => s.Stream.Index == mapping.SourceOffset.Scope.Stream.StreamIndex);
            SignedMediaTime offset = mapping.SourceOffset.Value!;
            PresentationRange coverage = mapping.Coverage.Value!;
            bool knownLeadingEmpty = file.SourceStart is not null && acquisition.Request.Inspection.Timeline.Origin.Value is { } origin &&
                origin.CompareTo(coverage.Start) <= 0;
            if ((native.ObservedRange.Start + offset).CompareTo(coverage.Start) > 0 && !knownLeadingEmpty ||
                (native.ObservedRange.End + offset).CompareTo(coverage.End) < 0)
                return Fail("AcquiredProbeCoverage");
            streams.Add(new(mapping, file.Probe, native));
        }
        if (streams.Count == 0) return Fail("AcquiredProbeStreams");
        return OperationResult<AcquiredMediaInspection>.Succeeded(new(acquisition, files, streams.ToArray()));
    }

    private static OperationResult<AcquiredMediaInspection> Fail(string field) => OperationResult<AcquiredMediaInspection>.Failed(
        new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "획득 snapshot과 로컬 검사 결과의 연결을 확인하지 못했습니다.", field));
}
