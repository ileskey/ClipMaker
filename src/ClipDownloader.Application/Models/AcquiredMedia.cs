using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

public sealed class AcquisitionRequest
{
    public Guid JobId { get; }
    public SelectedSourceSnapshot Selection { get; }
    public SelectedSourceInspection Inspection { get; }
    public MediaTimeRange Range { get; }
    public string WorkDirectory { get; }
    public AcquisitionRequest(Guid jobId, SelectedSourceSnapshot selection, SelectedSourceInspection inspection,
        MediaTimeRange range, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(range);
        string directory = AcquisitionContractValidation.AbsolutePath(workDirectory);
        if (jobId == Guid.Empty || jobId != inspection.Request.JobId || !selection.Matches(inspection.Request.Selection) ||
            !AcquisitionContractValidation.SameDirectory(directory, inspection.Request.WorkDirectory))
            throw new ArgumentException("Job, selection or workspace does not match the source inspection.", nameof(inspection));
        if (inspection.FrameRate.State != MediaInspectionState.Verified || inspection.FrameRate.Value != range.FrameRate ||
            selection.Selection.VideoFormat.FrameRate is { } declared && declared != range.FrameRate)
            throw new ArgumentException("Acquisition requires a confirmed, unchanged selected FPS.", nameof(range));
        if (selection.MetadataDuration is not { } duration || !range.TryValidateDuration(duration, out _))
            throw new ArgumentException("A range within the selected metadata duration is required.", nameof(range));
        JobId = jobId;
        Selection = selection;
        Inspection = inspection;
        Range = range;
        WorkDirectory = directory;
    }
}

public sealed class SourceStreamMapping
{
    public MediaInspectionFact<SignedMediaTime> SourceOffset { get; }
    public MediaInspectionFact<PresentationRange> Coverage { get; }
    public SourceStreamMapping(MediaInspectionFact<SignedMediaTime> sourceOffset, MediaInspectionFact<PresentationRange> coverage)
    {
        ArgumentNullException.ThrowIfNull(sourceOffset);
        ArgumentNullException.ThrowIfNull(coverage);
        if (sourceOffset.Scope.SnapshotId != coverage.Scope.SnapshotId || sourceOffset.Scope.Stream != coverage.Scope.Stream ||
            sourceOffset.Scope.TimeBasis != MediaTimeBasis.Native || coverage.Scope.TimeBasis != MediaTimeBasis.Native)
            throw new ArgumentException("Offset and coverage require the same native local stream snapshot.", nameof(coverage));
        if (coverage.Value is { } range)
        {
            var requiredKind = coverage.Scope.Stream.Kind == MediaStreamKind.Video
                ? MediaInspectionKind.Frames : MediaInspectionKind.Samples;
            if (!coverage.Scope.Covers(coverage.Scope.SnapshotId, coverage.Scope.Stream, requiredKind, MediaTimeBasis.Native, range))
                throw new ArgumentException("Verified coverage requires complete native frame/sample observations.", nameof(coverage));
            if (sourceOffset.Value is not null && !sourceOffset.Scope.Covers(sourceOffset.Scope.SnapshotId,
                sourceOffset.Scope.Stream, requiredKind, MediaTimeBasis.Native, range))
                throw new ArgumentException("Verified offset does not cover the declared coverage window.", nameof(sourceOffset));
        }
        SourceOffset = sourceOffset;
        Coverage = coverage;
    }
}

// Transfer success is not qualification or final clip success. Unknown facts remain unknown.
public sealed class AcquiredMedia
{
    public Guid AcquisitionId { get; }
    public AcquisitionRequest Request { get; }
    public IReadOnlyList<OwnedMediaFile> Files { get; }
    public IReadOnlyList<SourceStreamMapping> Mappings { get; }
    public AcquiredMedia(Guid acquisitionId, AcquisitionRequest request, IEnumerable<OwnedMediaFile> files,
        IEnumerable<SourceStreamMapping> mappings)
    {
        if (acquisitionId == Guid.Empty) throw new ArgumentException("Acquisition identity is required.", nameof(acquisitionId));
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mappings);
        var owned = AcquisitionContractValidation.Files(request.WorkDirectory, files, request.Selection);
        var items = mappings.ToArray();
        if (items.Any(mapping => mapping is null) || items.Select(mapping => mapping.SourceOffset.Scope.Stream).Distinct().Count() != items.Length)
            throw new ArgumentException("Stream mappings must be unique.", nameof(mappings));
        foreach (var mapping in items)
        {
            AcquisitionContractValidation.Fact(mapping.SourceOffset, acquisitionId, owned, request.Selection, MediaTimeBasis.Native);
            AcquisitionContractValidation.Fact(mapping.Coverage, acquisitionId, owned, request.Selection, MediaTimeBasis.Native);
        }
        AcquisitionId = acquisitionId;
        Request = request;
        Files = owned;
        Mappings = Array.AsReadOnly(items);
    }
}

public sealed record AcquisitionProgress
{
    public Guid JobId { get; }
    public double? Percent { get; }
    public string Message { get; }
    public AcquisitionProgress(Guid jobId, double? percent, string message)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("Job identity is required.", nameof(jobId));
        if (percent is { } value && (!double.IsFinite(value) || value < 0 || value > 100))
            throw new ArgumentOutOfRangeException(nameof(percent));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        JobId = jobId;
        Percent = percent;
        Message = message;
    }
}
