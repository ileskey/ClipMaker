using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SourceInspectionContractTests
{
    [Fact]
    public void MissingFpsInspectionNeedsNeitherClipJobNorResolvedRange()
    {
        var f = new F(missingRate: true);
        Assert.Null(f.Selection.Selection.VideoFormat.FrameRate);
        Assert.Equal(SourceInspectionPurpose.FrameRate | SourceInspectionPurpose.SourceTimeline, f.InspectionRequest.Purpose);
        var unknown = f.Inspect(knownRate: false);
        Assert.Equal(MediaInspectionState.Unknown, unknown.FrameRate.State);
        Assert.Throws<ArgumentException>(() => f.Request(unknown));
        var resolved = f.Inspect();
        Assert.Equal(F.Rate(), f.Request(resolved).Range.FrameRate);
        Assert.Equal(MediaInspectionState.Unknown, resolved.Timeline.Origin.State);
        Assert.Equal(StreamPresence.Unknown, resolved.Timeline.AudioPresence);
    }

    [Fact]
    public void SameGuidCannotMaskSourceModeRateOrSnapshotChanges()
    {
        var f = new F();
        Assert.True(f.Selection.Matches(F.Snapshot(f.Selection.SnapshotId)));
        foreach (var other in new[] { F.Snapshot(Guid.NewGuid()), F.Snapshot(f.Selection.SnapshotId, sourceId: "15430559"),
            F.Snapshot(f.Selection.SnapshotId, mode: ClipMode.Accurate), F.Snapshot(f.Selection.SnapshotId, rate: F.Rate(60)) })
        {
            Assert.False(f.Selection.Matches(other));
            Assert.Throws<ArgumentException>(() => new AcquisitionRequest(f.JobId, other, f.Inspect(), F.UserRange(), f.DirectoryPath));
        }
    }

    [Fact]
    public void InspectionRejectsWrongSnapshotFileRoleAndTimeBasis()
    {
        var f = new F();
        Assert.Throws<ArgumentException>(() => f.Inspect(rateScope: f.Scope(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => f.Inspect(rateScope: f.Scope(f.InspectionId, new(Guid.NewGuid(), 0, MediaStreamKind.Video))));
        Assert.Throws<ArgumentException>(() => f.Inspect(rateScope: f.Scope(f.InspectionId, new(f.File.Id, 1, MediaStreamKind.Audio))));
        var demuxOrigin = MediaInspectionFact<SignedMediaTime>.Verified(F.Time(0), f.Scope(f.InspectionId, basis: MediaTimeBasis.Demux));
        Assert.Throws<ArgumentException>(() => f.Inspect(timeline: f.Timeline(origin: demuxOrigin)));
        var axis = MediaInspectionFact<SourceAxisRelationship>.Verified(new("wrong", null, F.Time(0)), f.Scope(f.InspectionId));
        Assert.Throws<ArgumentException>(() => f.Inspect(timeline: f.Timeline(axis: axis)));
        Assert.Throws<ArgumentException>(() => f.Inspect(timeline: f.Timeline(presence: StreamPresence.Absent)));
    }

    [Fact]
    public void FileProvenancePreventsUsingSplitAudioResourceForVideoFps()
    {
        var f = new F(split: true);
        var audioFile = new OwnedMediaFile(f.File.Id, f.File.Path, ["a"]);
        Assert.Throws<ArgumentException>(() => f.Inspect(files: [audioFile]));
        var unselected = new OwnedMediaFile(f.File.Id, f.File.Path, ["other"]);
        Assert.Throws<ArgumentException>(() => f.Inspect(files: [unselected]));
    }

    [Fact]
    public void OwnershipAndBudgetValidationDoNotCreateFiles()
    {
        var f = new F();
        var files = new[] { f.File };
        var inspection = f.Inspect(files: files);
        files[0] = new(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "replacement.mp4"), ["v"]);
        Assert.Same(f.File, inspection.Files[0]);
        Assert.False(Directory.Exists(f.DirectoryPath));
        Assert.Throws<ArgumentException>(() => f.Inspect(files: [f.File, f.File]));
        Assert.Throws<ArgumentException>(() => f.Inspect(files: [new(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "..", "escaped.mp4"), ["v"])]));
        Assert.Throws<ArgumentException>(() => f.Inspect(files: [new(Guid.NewGuid(), f.DirectoryPath, ["v"])]));
        Assert.Throws<ArgumentException>(() => new OwnedMediaFile(Guid.NewGuid(), "relative.mp4", ["v"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceInspectionBudget(0, F.Time(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceInspectionBudget(1, F.Time(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectedSourceInspectionRequest(f.JobId, f.Selection, (SourceInspectionPurpose)4, f.DirectoryPath, f.InspectionRequest.Budget));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectedSourceInspectionRequest(f.JobId, f.Selection, 0, f.DirectoryPath, f.InspectionRequest.Budget));
    }

    [Fact]
    public void AcquisitionRejectsChangedKnownFpsJobWorkspaceAndMetadataRange()
    {
        var f = new F();
        var inspection = f.Inspect();
        Assert.Throws<ArgumentException>(() => new AcquisitionRequest(Guid.NewGuid(), f.Selection, inspection, F.UserRange(), f.DirectoryPath));
        Assert.Throws<ArgumentException>(() => new AcquisitionRequest(f.JobId, f.Selection, inspection, F.UserRange(), Path.Combine(f.DirectoryPath, "other")));
        Assert.Throws<ArgumentException>(() => new AcquisitionRequest(f.JobId, f.Selection, f.Inspect(rate: F.Rate(60)), F.UserRange(F.Rate(60)), f.DirectoryPath));
        Assert.True(TimecodeInput.TryParse("00:02:01:00", out var input, out _));
        Assert.True(Timecode.TryCreate(input, F.Rate(), out var end, out _));
        Assert.True(MediaTimeRange.TryCreate(F.UserRange().Start, end, out var range, out _));
        Assert.Throws<ArgumentException>(() => new AcquisitionRequest(f.JobId, f.Selection, inspection, range, f.DirectoryPath));
    }
}
