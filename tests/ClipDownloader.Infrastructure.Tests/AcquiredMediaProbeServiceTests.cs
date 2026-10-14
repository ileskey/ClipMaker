using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AcquiredMediaProbeServiceTests
{
    private sealed class Probe(string fault, ProbedInitialReference reference) : IMediaProbe
    {
        internal int Calls;
        public Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken token)
        {
            Calls++;
            if (fault == "cancel") return Task.FromResult(MediaOperationResult<MediaProbeResult>.Cancelled());
            if (fault == "termination") return Task.FromResult(MediaOperationResult<MediaProbeResult>.Failed(
                new(MediaFailureKind.TerminationUnconfirmed, "safe", "ProbeTermination")));
            var raw = reference.Observations.ToShared();
            var streams = raw.Streams.Select(s => s with { DeclaredRate = fault == "fps" ? F.Rate(60) : F.Rate(30) });
            raw = new(streams, raw.Frames, raw.Packets, true, true);
            if (fault == "pts") raw = new(raw.Streams, raw.Frames.Select(f => f with { Pts = null }), raw.Packets, true, true);
            var container = new Mp4FileTimingEvidence(reference.ContainerTiming, [],
                MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(reference.NativeSamples!.Data!));
            var substituted = fault == "snapshot" ? new MediaProbeRequest(Guid.NewGuid(), request.FileId,
                request.Path, request.Content, request.Detail, request.Range, true) : request;
            return Task.FromResult(MediaOperationResult<MediaProbeResult>.Succeeded(new(substituted, "fixture", raw, container)));
        }
    }

    [Theory]
    [InlineData("success", null)]
    [InlineData("content", "AcquiredProbePrerequisite")]
    [InlineData("mapping", "AcquiredProbePrerequisite")]
    [InlineData("snapshot", "AcquiredProbeIdentity")]
    [InlineData("coverage", "AcquiredProbeCoverage")]
    [InlineData("pts", "NativeFramePtsMissing")]
    [InlineData("fps", "SelectedFrameRateChanged")]
    [InlineData("termination", "ProbeTermination")]
    [InlineData("cancel", null)]
    public async Task LocalProbeBindsCapturedIdentityMappingAndScopeWithoutQualifyingProcessing(string fault, string? field)
    {
        var f = new F();
        var file = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(f.DirectoryPath, "target.mp4"), ["v"],
            fault == "content" ? null : new(1116, new string('a', 64)));
        var reference = HlsNativeReferenceVerifierTests.Reference(file);
        var acquisitionId = Guid.NewGuid();
        PresentationRange.TryCreate(F.Time(2), F.Time(fault == "coverage" ? 3 : 61, fault == "coverage" ? 1 : 30), out var coverage, out _);
        var scope = f.Scope(acquisitionId, new(file.Id, 0, MediaStreamKind.Video), MediaInspectionKind.Frames,
            range: coverage);
        var offset = fault == "mapping" ? MediaInspectionFact<SignedMediaTime>.Unknown(scope, MediaInspectionIssue.NotInspected) :
            MediaInspectionFact<SignedMediaTime>.Verified(SignedMediaTime.Zero, scope);
        var acquired = new AcquiredMedia(acquisitionId, f.Request(), [file],
            [new(offset, MediaInspectionFact<PresentationRange>.Verified(coverage!, scope))]);
        var probe = new Probe(fault, reference);
        var result = await new AcquiredMediaProbeService(probe).InspectAsync(acquired, TestContext.Current.CancellationToken);
        if (fault == "success")
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Same(acquired, result.Data!.Acquisition);
            var bound = Assert.Single(result.Data.Streams);
            Assert.Equal(file.Content, bound.Probe.Request.Content);
            Assert.Equal(acquisitionId, bound.Probe.Request.SnapshotId);
            Assert.Same(acquired.Mappings[0], bound.Mapping);
        }
        else if (fault == "cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Data); Assert.Null(result.Error); }
        else
        {
            Assert.Null(result.Data); Assert.Equal(field, result.Error!.Field);
            Assert.Equal(fault == "termination" ? OperationErrorCode.ProcessTerminationFailed : fault == "fps" ?
                OperationErrorCode.InvalidMetadata : OperationErrorCode.VerificationFailed, result.Error.Code);
        }
        Assert.Equal(fault is "content" or "mapping" ? 0 : 1, probe.Calls);
    }
}
