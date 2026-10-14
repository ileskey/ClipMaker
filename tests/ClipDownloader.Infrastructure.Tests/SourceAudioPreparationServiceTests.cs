using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Infrastructure.Processing;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SourceAudioPreparationServiceTests
{
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
