using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

internal sealed class AcquisitionContractFixture
{
    internal Guid JobId { get; } = Guid.NewGuid();
    internal Guid InspectionId { get; } = Guid.NewGuid();
    internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "clip-contract", Guid.NewGuid().ToString("N"));
    internal SelectedSourceSnapshot Selection { get; }
    internal OwnedMediaFile File { get; }
    internal SelectedSourceInspectionRequest InspectionRequest { get; }

    internal AcquisitionContractFixture(bool missingRate = false, bool split = false)
    {
        Selection = Snapshot(Guid.NewGuid(), missingRate: missingRate, split: split);
        File = new(Guid.NewGuid(), Path.Combine(DirectoryPath, "initial.mp4"), ["v"]);
        InspectionRequest = new(JobId, Selection, SourceInspectionPurpose.FrameRate | SourceInspectionPurpose.SourceTimeline,
            DirectoryPath, new(1024 * 1024, Time(6)));
    }

    internal static SignedMediaTime Time(long n, long d = 1)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }
    internal static FrameRate Rate(long n = 30, long d = 1)
    {
        Assert.True(FrameRate.TryCreate(n, d, out var rate, out _));
        return rate;
    }
    internal static PresentationRange Range(long start = 0, long end = 10)
    {
        Assert.True(PresentationRange.TryCreate(Time(start), Time(end), out var range, out _));
        return range;
    }
    internal static MediaTimeRange UserRange(FrameRate? rate = null)
    {
        Assert.True(TimecodeInput.TryParse("00:00:01:00", out var startInput, out _));
        Assert.True(TimecodeInput.TryParse("00:00:02:00", out var endInput, out _));
        Assert.True(Timecode.TryCreate(startInput, rate ?? Rate(), out var start, out _));
        Assert.True(Timecode.TryCreate(endInput, rate ?? Rate(), out var end, out _));
        Assert.True(MediaTimeRange.TryCreate(start, end, out var range, out _));
        return range;
    }

    internal static SelectedSourceSnapshot Snapshot(Guid id, string sourceId = "15430558", ClipMode mode = ClipMode.Fast,
        FrameRate? rate = null, bool missingRate = false, bool split = false)
    {
        Assert.True(VideoSourceReference.TryCreate(VideoPlatform.Chzzk, sourceId, out var source));
        var video = new VideoFormat("v", 1920, 1080, missingRate ? null : rate ?? Rate(), "h264", split ? null : "aac",
            StreamPresence.Present, split ? StreamPresence.Absent : StreamPresence.Present);
        VideoFormat? audio = split ? new("a", null, null, null, null, "aac", StreamPresence.Absent, StreamPresence.Present) : null;
        var info = new VideoInfo(source, null, TimeSpan.FromSeconds(120), null, null, null, VideoState.Completed,
            audio is null ? [video] : [video, audio], []);
        var selection = FormatSelection.Create(info, QualityOption.Best, video, audio, FormatSelectionReason.RequestedBest);
        Assert.Equal(OperationStatus.Succeeded, selection.Status);
        var result = SelectedSourceSnapshot.Create(id, info, selection.Data!, mode);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        return result.Data!;
    }

    internal MediaObservationScope Scope(Guid snapshot, MediaStreamReference? stream = null,
        MediaInspectionKind kind = MediaInspectionKind.Summary, MediaTimeBasis basis = MediaTimeBasis.Native,
        PresentationRange? range = null, ObservationCompleteness completeness = ObservationCompleteness.Complete) =>
        new(snapshot, stream ?? new(File.Id, 0, MediaStreamKind.Video), kind, basis, range, completeness);

    internal SourceTimelineDescriptor Timeline(MediaInspectionFact<SignedMediaTime>? origin = null,
        MediaInspectionFact<SourceAxisRelationship>? axis = null, StreamPresence presence = StreamPresence.Unknown) =>
        new(origin ?? MediaInspectionFact<SignedMediaTime>.Unknown(Scope(InspectionId), MediaInspectionIssue.NotInspected),
            axis ?? MediaInspectionFact<SourceAxisRelationship>.Unknown(Scope(InspectionId), MediaInspectionIssue.NotInspected),
            MediaInspectionFact<SignedMediaTime>.Unknown(Scope(InspectionId), MediaInspectionIssue.NotInspected), presence);

    internal SelectedSourceInspection Inspect(bool knownRate = true, FrameRate? rate = null,
        MediaObservationScope? rateScope = null, SourceTimelineDescriptor? timeline = null, IEnumerable<OwnedMediaFile>? files = null)
    {
        var scope = rateScope ?? Scope(InspectionId);
        var fact = knownRate ? MediaInspectionFact<FrameRate>.Verified(rate ?? Rate(), scope) :
            MediaInspectionFact<FrameRate>.Unknown(scope, MediaInspectionIssue.NotInspected);
        return new(InspectionId, InspectionRequest, files ?? [File], fact, timeline ?? Timeline());
    }

    internal AcquisitionRequest Request(SelectedSourceInspection? inspection = null) =>
        new(JobId, Selection, inspection ?? Inspect(), UserRange(), DirectoryPath);
}
