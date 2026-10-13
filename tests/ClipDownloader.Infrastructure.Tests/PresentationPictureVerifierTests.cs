using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class PresentationPictureVerifierTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("values")]
    [InlineData("time")]
    [InlineData("duration")]
    [InlineData("dimensions")]
    [InlineData("representation")]
    [InlineData("native")]
    public void DecodedValuesAndNativeTimeMustMatchIndependentlyOfEncodedPayload(string fault)
    {
        var reference = HlsNativeReferenceVerifierTests.Reference();
        var source = new VerifiedHlsNativeReference(reference,
            [new(reference.Observations.Streams[0], reference.NativeSamples!.Data!.Configurations[0], F.Range(2, 3))]);
        string a = new('A', 64), b = new('B', 64);
        var movie = new DecodedPictureObservations(F.Time(1, 6000), 2, 2,
            [new(12000, 100, 6, a), new(fault == "native" ? 12101 : 12100, 100, 6, b)]);
        var transport = new DecodedPictureObservations(F.Time(1, 90000), fault == "dimensions" ? 4 : 2, 2,
            [new(fault == "time" ? 180001 : 180000, fault == "duration" ? 1499 : 1500, 6, fault == "values" ? b : a), new(181500, 1500, 6, b)]);
        var carrier = new H264CarrierObservations(F.Time(1, 90000), [], fault == "representation" ? null : "yuv420p", 2, 2);
        var result = new PresentationPictureVerifier().Verify(source, carrier, movie, transport);
        if (fault == "success")
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(2, result.Data!.PictureCount);
        }
        else { Assert.Null(result.Data); Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code); }
    }
}
