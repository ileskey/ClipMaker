using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

[Flags]
public enum SourceInspectionPurpose { FrameRate = 1, SourceTimeline = 2 }

public sealed record SourceInspectionBudget
{
    public long MaximumBytes { get; }
    public SignedMediaTime MaximumPresentationSpan { get; }
    public SourceInspectionBudget(long maximumBytes, SignedMediaTime maximumPresentationSpan)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentNullException.ThrowIfNull(maximumPresentationSpan);
        if (maximumPresentationSpan.Numerator <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPresentationSpan));
        MaximumBytes = maximumBytes;
        MaximumPresentationSpan = maximumPresentationSpan;
    }
}

public sealed class SelectedSourceInspectionRequest
{
    public Guid JobId { get; }
    public SelectedSourceSnapshot Selection { get; }
    public SourceInspectionPurpose Purpose { get; }
    public string WorkDirectory { get; }
    public SourceInspectionBudget Budget { get; }
    public SelectedSourceInspectionRequest(Guid jobId, SelectedSourceSnapshot selection, SourceInspectionPurpose purpose,
        string workDirectory, SourceInspectionBudget budget)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("Job identity is required.", nameof(jobId));
        ArgumentNullException.ThrowIfNull(selection);
        if (purpose == 0 || (purpose & ~(SourceInspectionPurpose.FrameRate | SourceInspectionPurpose.SourceTimeline)) != 0)
            throw new ArgumentOutOfRangeException(nameof(purpose));
        ArgumentNullException.ThrowIfNull(budget);
        JobId = jobId;
        Selection = selection;
        Purpose = purpose;
        WorkDirectory = AcquisitionContractValidation.AbsolutePath(workDirectory);
        Budget = budget;
    }
}

public sealed record SourceAxisRelationship
{
    public string VideoFormatId { get; }
    public string? AudioFormatId { get; }
    public SignedMediaTime AudioToVideoOffset { get; }
    public SourceAxisRelationship(string videoFormatId, string? audioFormatId, SignedMediaTime audioToVideoOffset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFormatId);
        if (audioFormatId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(audioFormatId);
        ArgumentNullException.ThrowIfNull(audioToVideoOffset);
        VideoFormatId = videoFormatId;
        AudioFormatId = audioFormatId;
        AudioToVideoOffset = audioToVideoOffset;
    }
}

public sealed class SourceTimelineDescriptor
{
    public MediaInspectionFact<SignedMediaTime> Origin { get; }
    public MediaInspectionFact<SourceAxisRelationship> SharedAxis { get; }
    public MediaInspectionFact<SignedMediaTime> NativeEnd { get; }
    public StreamPresence AudioPresence { get; }
    public SourceTimelineDescriptor(MediaInspectionFact<SignedMediaTime> origin,
        MediaInspectionFact<SourceAxisRelationship> sharedAxis, MediaInspectionFact<SignedMediaTime> nativeEnd,
        StreamPresence audioPresence)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(sharedAxis);
        ArgumentNullException.ThrowIfNull(nativeEnd);
        if (!Enum.IsDefined(audioPresence)) throw new ArgumentOutOfRangeException(nameof(audioPresence));
        Origin = origin;
        SharedAxis = sharedAxis;
        NativeEnd = nativeEnd;
        AudioPresence = audioPresence;
    }
}

public sealed class SelectedSourceInspection
{
    public Guid InspectionId { get; }
    public SelectedSourceInspectionRequest Request { get; }
    public IReadOnlyList<OwnedMediaFile> Files { get; }
    public MediaInspectionFact<FrameRate> FrameRate { get; }
    public SourceTimelineDescriptor Timeline { get; }
    public SelectedSourceInspection(Guid inspectionId, SelectedSourceInspectionRequest request, IEnumerable<OwnedMediaFile> files,
        MediaInspectionFact<FrameRate> frameRate, SourceTimelineDescriptor timeline)
    {
        if (inspectionId == Guid.Empty) throw new ArgumentException("Inspection identity is required.", nameof(inspectionId));
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeline);
        var owned = AcquisitionContractValidation.Files(request.WorkDirectory, files, request.Selection);
        AcquisitionContractValidation.Fact(frameRate, inspectionId, owned, request.Selection);
        AcquisitionContractValidation.Fact(timeline.Origin, inspectionId, owned, request.Selection, MediaTimeBasis.Native);
        AcquisitionContractValidation.Fact(timeline.SharedAxis, inspectionId, owned, request.Selection, MediaTimeBasis.Native);
        AcquisitionContractValidation.Fact(timeline.NativeEnd, inspectionId, owned, request.Selection, MediaTimeBasis.Native);
        var selected = request.Selection.Selection;
        if (frameRate.Scope.Stream.Kind != MediaStreamKind.Video)
            throw new ArgumentException("Selected FPS must come from a video observation.", nameof(frameRate));
        if (timeline.SharedAxis.Value is { } axis &&
            (axis.VideoFormatId != selected.VideoFormat.Id || axis.AudioFormatId != selected.AudioFormat?.Id))
            throw new ArgumentException("Shared axis refers to a different selected pair.", nameof(timeline));
        if (timeline.AudioPresence == StreamPresence.Absent &&
            (selected.AudioFormat is not null || selected.VideoFormat.AudioStream == StreamPresence.Present))
            throw new ArgumentException("Observed audio absence contradicts the selection.", nameof(timeline));
        InspectionId = inspectionId;
        Request = request;
        Files = owned;
        FrameRate = frameRate;
        Timeline = timeline;
    }
}
