using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AcquiredMediaContractTests
{
    private static SourceStreamMapping Mapping(F f, Guid acquisition, MediaInspectionState state = MediaInspectionState.Unknown)
    {
        var scope = f.Scope(acquisition, kind: MediaInspectionKind.Frames, range: F.Range());
        var offset = state == MediaInspectionState.Verified ? MediaInspectionFact<SignedMediaTime>.Verified(F.Time(90), scope) :
            MediaInspectionFact<SignedMediaTime>.Unknown(scope, MediaInspectionIssue.NotInspected);
        var coverage = state == MediaInspectionState.Verified ? MediaInspectionFact<PresentationRange>.Verified(F.Range(), scope) :
            MediaInspectionFact<PresentationRange>.Contradicted(scope, MediaInspectionIssue.ConflictingMeasurements);
        return new(offset, coverage);
    }

    [Fact]
    public void TransferSnapshotPreservesUnknownAndContradictedFactsAndCopiesCollections()
    {
        var f = new F();
        Guid id = Guid.NewGuid();
        var mapping = Mapping(f, id);
        var files = new List<OwnedMediaFile> { f.File };
        var mappings = new List<SourceStreamMapping> { mapping };
        var acquired = new AcquiredMedia(id, f.Request(), files, mappings);
        files.Clear();
        mappings.Clear();
        Assert.Single(acquired.Files);
        Assert.Single(acquired.Mappings);
        Assert.Null(acquired.Mappings[0].SourceOffset.Value);
        Assert.Equal(MediaInspectionState.Unknown, acquired.Mappings[0].SourceOffset.State);
        Assert.Equal(MediaInspectionState.Contradicted, acquired.Mappings[0].Coverage.State);
        Assert.Equal(OperationStatus.Succeeded, OperationResult<AcquiredMedia>.Succeeded(acquired).Status);
        Assert.Equal(MediaInspectionState.Unknown, acquired.Request.Inspection.Timeline.SharedAxis.State);
    }

    [Fact]
    public void RejectsForeignObservationSnapshotAndDuplicateMappings()
    {
        var f = new F();
        Guid id = Guid.NewGuid();
        var mapping = Mapping(f, id);
        Assert.Throws<ArgumentException>(() => new AcquiredMedia(Guid.NewGuid(), f.Request(), [f.File], [mapping]));
        Assert.Throws<ArgumentException>(() => new AcquiredMedia(id, f.Request(), [f.File], [mapping, mapping]));
        var otherFile = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "other.mp4"), ["v"]);
        Assert.Throws<ArgumentException>(() => new AcquiredMedia(id, f.Request(), [otherFile], [mapping]));
        Assert.Single(new AcquiredMedia(id, f.Request(), [f.File], [Mapping(f, id, MediaInspectionState.Verified)]).Mappings);
    }

    [Fact]
    public void VerifiedCoverageRequiresNativeFrameOrSampleWindowAndScopedOffset()
    {
        var f = new F();
        Guid id = Guid.NewGuid();
        var frameScope = f.Scope(id, kind: MediaInspectionKind.Frames, range: F.Range());
        var offset = MediaInspectionFact<SignedMediaTime>.Verified(F.Time(90), frameScope);
        var summary = f.Scope(id, range: F.Range());
        Assert.Throws<ArgumentException>(() => new SourceStreamMapping(offset, MediaInspectionFact<PresentationRange>.Verified(F.Range(), summary)));
        Assert.Throws<ArgumentException>(() => new SourceStreamMapping(offset, MediaInspectionFact<PresentationRange>.Verified(F.Range(-1, 10), frameScope)));
        var limitedOffset = MediaInspectionFact<SignedMediaTime>.Verified(F.Time(90), f.Scope(id, kind: MediaInspectionKind.Frames, range: F.Range(0, 5)));
        Assert.Throws<ArgumentException>(() => new SourceStreamMapping(limitedOffset, MediaInspectionFact<PresentationRange>.Verified(F.Range(), frameScope)));
        var demux = f.Scope(id, kind: MediaInspectionKind.Frames, basis: MediaTimeBasis.Demux, range: F.Range());
        Assert.Throws<ArgumentException>(() => new SourceStreamMapping(MediaInspectionFact<SignedMediaTime>.Verified(F.Time(90), demux), MediaInspectionFact<PresentationRange>.Verified(F.Range(), frameScope)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidProgressWithoutInventingUnknownPercentage(double percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AcquisitionProgress(Guid.NewGuid(), percent, "획득 중"));

    [Fact]
    public void UnknownProgressAndSourceFormatListRemainExplicitSnapshots()
    {
        var f = new F();
        var progress = new AcquisitionProgress(f.JobId, null, "획득 중");
        Assert.Null(progress.Percent);
        string[] formats = ["v"];
        var file = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "copy.mp4"), formats);
        formats[0] = "other";
        Assert.Equal("v", file.SourceFormatIds[0]);
        Assert.Throws<ArgumentException>(() => new OwnedMediaFile(Guid.NewGuid(), file.Path, ["v", "v"]));
    }
}
