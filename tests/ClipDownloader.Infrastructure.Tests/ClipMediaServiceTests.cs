using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ClipMediaServiceTests
{
    private sealed class Probe(string fault) : IMediaProbe
    {
        internal int Calls;
        public Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(fault == "cancel" ? MediaOperationResult<MediaProbeResult>.Cancelled() : MediaOperationResult<MediaProbeResult>.Failed(
                new(fault == "termination" ? MediaFailureKind.TerminationUnconfirmed : MediaFailureKind.InvalidMedia, "safe", "FixtureProbe")));
        }
    }
    [Theory]
    [InlineData("termination")]
    [InlineData("cancel")]
    [InlineData("invalid")]
    public async Task CompositionNeverProcessesAfterFailedProbeAndPreservesTerminationKnowledge(string fault)
    {
        var acquired = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection.Acquisition;
        var probe = new Probe(fault); var service = new ClipMediaService(probe, null!, new(), null!);
        string staging = Path.Combine(acquired.Request.WorkDirectory, "staging.mp4");
        var result = await service.ProcessAsync(acquired, Guid.NewGuid(), staging, null, TestContext.Current.CancellationToken);
        Assert.Null(result.Data); Assert.Equal(1, probe.Calls); Assert.False(File.Exists(staging));
        var resources = service.GetOwnedResources(acquired.Request.JobId);
        Assert.Empty(resources.Files); Assert.Equal(fault == "termination", resources.HasUnconfirmedProcess);
        if (fault == "cancel") Assert.Equal(OperationStatus.Cancelled, result.Status);
        else Assert.Equal(fault == "termination" ? OperationErrorCode.ProcessTerminationFailed : OperationErrorCode.VerificationFailed, result.Error!.Code);
        if (fault == "termination")
        {
            var next = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection.Acquisition;
            Assert.Equal("PreviousMediaTermination", (await service.ProcessAsync(next, Guid.NewGuid(), Path.Combine(next.Request.WorkDirectory, "staging.mp4"),
                null, TestContext.Current.CancellationToken)).Error!.Field);
            Assert.Equal(1, probe.Calls);
        }
    }
}
