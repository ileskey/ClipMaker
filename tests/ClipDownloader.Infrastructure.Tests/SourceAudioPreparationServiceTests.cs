using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Processing;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SourceAudioPreparationServiceTests
{
    [Theory]
    [InlineData("missing-proof", "SourceDecodedFrameLayout")]
    [InlineData("later-trim", "SourceDecodedFrameLayout")]
    [InlineData("proven-trim", "SourceStartPrefixRead")]
    public async Task PartialLeadingFrameRequiresNativeSkipProofAndStillRequiresCapturedSourceStart(string fault, string field)
    {
        var fixture = ClipProcessingQualifierTests.Fixture(ClipMode.Accurate);
        var audio = fixture.Inspection.Streams.Single(s => s.Native.Stream.Kind == MediaStreamKind.Audio);
        var files = fixture.Inspection.Files.Select(file =>
        {
            var audioFrames = file.Probe.Observations.Frames.Where(f => f.StreamIndex == audio.Native.Stream.Index).ToArray();
            var changed = fault == "later-trim" ? audioFrames[1] : audioFrames[0];
            var observations = new MediaProbeObservations(file.Probe.Observations.Streams,
                file.Probe.Observations.Frames.Select(f => ReferenceEquals(f, changed) ? f with { Duration = 448, SampleCount = 448 } : f),
                file.Probe.Observations.Packets, true, true);
            var probe = new MediaProbeResult(file.Probe.Request, file.Probe.ProbeVersion, observations, file.Probe.Container);
            var native = new Mp4NativeObservation(file.Native.Streams.Select(s => s.Stream.Kind == MediaStreamKind.Audio
                ? s with { LeadingSkippedSamples = fault == "missing-proof" ? 0 : 1600 } : s));
            return file with { Probe = probe, Native = native };
        }).ToArray();
        var inspection = AcquiredMediaInspection.Create(fixture.Inspection.Acquisition, files); Assert.Equal(OperationStatus.Succeeded, inspection.Status);
        var processor = new FfmpegMediaProcessingService(null!, new(), null!); var operation = Guid.NewGuid();
        var result = await new SourceAudioPreparationService(processor).PrepareAsync(inspection.Data!, operation, Guid.NewGuid(),
            fixture.Preparation.Value!.DecodedPcm.Path, TestContext.Current.CancellationToken);
        Assert.Null(result.Data); Assert.Equal(field, result.Error!.Field); Assert.Empty(processor.GetOwnedResources(operation).Files);
    }
    [Theory]
    [InlineData(ClipMode.Fast, "", "SourceAudioPreparationStrategy")]
    [InlineData(ClipMode.Accurate, "prefix", "SourceAudioPreparationIdentity")]
    [InlineData(ClipMode.Accurate, "", "SourceStartPrefixRead")]
    public async Task UnverifiedSourceStartDoesNotCreatePreparedPcm(ClipMode mode, string fault, string field)
    {
        var fixture = ClipProcessingQualifierTests.Fixture(mode, fault);
        var processor = new FfmpegMediaProcessingService(null!, new(), null!);
        var service = new SourceAudioPreparationService(processor);
        var operation = Guid.NewGuid();
        var result = await service.PrepareAsync(fixture.Inspection, operation, Guid.NewGuid(), fixture.Preparation.Value!.DecodedPcm.Path,
            TestContext.Current.CancellationToken);
        Assert.Null(result.Data); Assert.Equal(field, result.Error!.Field);
        Assert.Empty(processor.GetOwnedResources(operation).Files);
        Assert.False(File.Exists(fixture.Preparation.Value.DecodedPcm.Path));
    }
}
