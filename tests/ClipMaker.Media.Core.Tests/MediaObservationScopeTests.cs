using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;

namespace ClipMaker.Media.Core.Tests;

public sealed class MediaObservationScopeTests
{
    private static SignedMediaTime Time(long n, long d = 1)
    {
        Assert.True(SignedMediaTime.TryCreate(n, d, out var time, out _));
        return time;
    }
    private static PresentationRange Range(long start, long end)
    {
        Assert.True(PresentationRange.TryCreate(Time(start), Time(end), out var range, out _));
        return range;
    }

    [Fact]
    public void CoverageRequiresSameSnapshotStreamKindBasisAndContainedWindow()
    {
        Guid snapshot = Guid.NewGuid();
        Guid file = Guid.NewGuid();
        var stream = new MediaStreamReference(file, 0, MediaStreamKind.Video);
        var scope = new MediaObservationScope(snapshot, stream, MediaInspectionKind.Frames,
            MediaTimeBasis.Native, Range(-2, 12), ObservationCompleteness.Complete);
        Assert.True(scope.Covers(snapshot, new(file, 0, MediaStreamKind.Video), MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(-2, 12)));
        Assert.True(scope.Covers(snapshot, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 10)));
        Assert.False(scope.Covers(Guid.NewGuid(), stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 10)));
        Assert.False(scope.Covers(snapshot, new(Guid.NewGuid(), 0, MediaStreamKind.Video), MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 10)));
        Assert.False(scope.Covers(snapshot, new(file, 1, MediaStreamKind.Video), MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 10)));
        Assert.False(scope.Covers(snapshot, stream, MediaInspectionKind.Summary, MediaTimeBasis.Native, Range(0, 10)));
        Assert.False(scope.Covers(snapshot, stream, MediaInspectionKind.Frames, MediaTimeBasis.Demux, Range(0, 10)));
        Assert.False(scope.Covers(snapshot, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(-3, 10)));
        Assert.False(scope.Covers(snapshot, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 13)));
    }

    [Fact]
    public void PartialOrUnboundedSummaryDoesNotEstablishRequiredWindowCoverage()
    {
        Guid id = Guid.NewGuid();
        var stream = new MediaStreamReference(Guid.NewGuid(), 0, MediaStreamKind.Video);
        var partial = new MediaObservationScope(id, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native,
            Range(0, 10), ObservationCompleteness.Partial);
        Assert.False(partial.Covers(id, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(2, 8)));
        var summary = new MediaObservationScope(id, stream, MediaInspectionKind.Summary, MediaTimeBasis.Demux,
            null, ObservationCompleteness.Complete);
        Assert.False(summary.Covers(id, stream, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(2, 8)));
        Assert.False(summary.Covers(id, stream, MediaInspectionKind.Summary, MediaTimeBasis.Demux, Range(2, 8)));
    }

    [Fact]
    public void FactsKeepUnresolvedStatesDistinctAndCannotVerifyPartialObservation()
    {
        Guid id = Guid.NewGuid();
        var stream = new MediaStreamReference(Guid.NewGuid(), 0, MediaStreamKind.Audio);
        var complete = new MediaObservationScope(id, stream, MediaInspectionKind.Samples, MediaTimeBasis.Native,
            Range(0, 10), ObservationCompleteness.Complete);
        var partial = new MediaObservationScope(id, stream, MediaInspectionKind.Samples, MediaTimeBasis.Native,
            Range(0, 10), ObservationCompleteness.Partial);
        var known = MediaInspectionFact<SignedMediaTime>.Verified(SignedMediaTime.Zero, complete);
        Assert.Equal(MediaInspectionState.Verified, known.State);
        Assert.Equal(SignedMediaTime.Zero, known.Value);
        Assert.Null(known.Issue);
        Assert.Same(complete, known.Scope);
        var unknown = MediaInspectionFact<SignedMediaTime>.Unknown(partial, MediaInspectionIssue.MissingTimestamp);
        var contradiction = MediaInspectionFact<SignedMediaTime>.Contradicted(complete, MediaInspectionIssue.ConflictingMeasurements);
        Assert.Equal(MediaInspectionState.Unknown, unknown.State);
        Assert.Equal(MediaInspectionState.Contradicted, contradiction.State);
        Assert.Null(unknown.Value);
        Assert.Null(contradiction.Value);
        Assert.Equal(MediaInspectionIssue.MissingTimestamp, unknown.Issue);
        Assert.Equal(MediaInspectionIssue.ConflictingMeasurements, contradiction.Issue);
        Assert.Throws<ArgumentException>(() => MediaInspectionFact<SignedMediaTime>.Verified(SignedMediaTime.Zero, partial));
        Assert.Throws<ArgumentNullException>(() => MediaInspectionFact<SignedMediaTime>.Verified(null!, complete));
        Assert.Throws<ArgumentOutOfRangeException>(() => MediaInspectionFact<SignedMediaTime>.Unknown(complete, (MediaInspectionIssue)99));
    }

    [Fact]
    public void RejectsInvalidIdentityInspectionRoleAndMissingNativeWindow()
    {
        var audio = new MediaStreamReference(Guid.NewGuid(), 0, MediaStreamKind.Audio);
        Assert.Throws<ArgumentException>(() => new MediaStreamReference(Guid.Empty, 0, MediaStreamKind.Video));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaStreamReference(Guid.NewGuid(), -1, MediaStreamKind.Video));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaStreamReference(Guid.NewGuid(), 0, (MediaStreamKind)99));
        Assert.Throws<ArgumentException>(() => new MediaObservationScope(Guid.Empty, audio, MediaInspectionKind.Summary, MediaTimeBasis.Demux, null, ObservationCompleteness.Complete));
        Assert.Throws<ArgumentException>(() => new MediaObservationScope(Guid.NewGuid(), audio, MediaInspectionKind.Samples, MediaTimeBasis.Native, null, ObservationCompleteness.Complete));
        Assert.Throws<ArgumentException>(() => new MediaObservationScope(Guid.NewGuid(), audio, MediaInspectionKind.Frames, MediaTimeBasis.Native, Range(0, 1), ObservationCompleteness.Complete));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaObservationScope(Guid.NewGuid(), audio, MediaInspectionKind.Summary, (MediaTimeBasis)99, null, ObservationCompleteness.Complete));
    }
}
