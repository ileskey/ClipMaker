using ClipMaker.Media.Core.Models;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class IndexedNativeAcquisitionVerifierTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("index")]
    [InlineData("support")]
    [InlineData("configuration")]
    [InlineData("movieclock")]
    [InlineData("trackclock")]
    [InlineData("timing")]
    public void TargetNeedsActualSamplesStableHeaderAndIndexNativeAgreement(string fault)
    {
        var reference = HlsNativeReferenceVerifierTests.Reference();
        var initial = new HlsNativeReferenceVerifier().Verify(reference).Data!;
        var range = initial.Streams.Single().ObservedRange;
        var target = reference with { Reference = new IndexedCollectedReference(reference.Reference.File, 1116, 1116,
            new string('A', 64), range, fault == "index" ? F.Range(0, 2) : range) };
        if (fault == "configuration") target = target with { NativeSamples = OperationResult<Mp4NativeSampleEvidence>.Succeeded(
            new(target.NativeSamples!.Data!.Configurations.Select(c => c with { ConfigSha256 = new string('B', 64) }), target.NativeSamples.Data.Samples)) };
        if (fault == "movieclock") target = target with { ContainerTiming = target.ContainerTiming with { MovieTimescale = 1000 } };
        if (fault == "trackclock") target = target with { ContainerTiming = target.ContainerTiming with
            { Tracks = [target.ContainerTiming.Tracks.Single() with { MediaTimescale = 5000 }] } };
        if (fault == "timing") target = target with { Observations = new(target.Observations.Streams, target.Observations.Frames,
            target.Observations.Packets.Select(p => p with { Pts = p.Pts + 1 }), true) };
        var result = new IndexedNativeAcquisitionVerifier().Verify(initial, target, fault == "support" ? F.Range(2, 3) : range);
        if (fault == "success") Assert.Equal(OperationStatus.Succeeded, result.Status);
        else { Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.Data); }
    }
}
