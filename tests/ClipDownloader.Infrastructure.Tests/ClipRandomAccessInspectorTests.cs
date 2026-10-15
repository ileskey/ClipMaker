using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ClipRandomAccessInspectorTests
{
    [Theory]
    [InlineData("idr")]
    [InlineData("non-idr")]
    [InlineData("mixed")]
    [InlineData("changed")]
    [InlineData("cancel")]
    public async Task ScopedRandomAccessNeedsCapturedBytesAndOnlyIdrSlices(string fault)
    {
        var original = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection;
        var acquired = original.Acquisition;
        var owned = Assert.Single(acquired.Files);
        Directory.CreateDirectory(acquired.Request.WorkDirectory);
        try
        {
            byte[] bytes = fault == "mixed" ? [0, 0, 0, 2, 0x65, 0x88, 0, 0, 0, 2, 0x41, 0x88] :
                [0, 0, 0, 2, fault == "non-idr" ? (byte)0x41 : (byte)0x65, 0x88];
            File.WriteAllBytes(owned.Path, bytes);
            var identity = new MediaFileContentIdentity(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
            var updatedFile = new OwnedMediaFile(owned.Id, owned.Path, owned.SourceFormatIds, identity);
            var updated = new AcquiredMedia(acquired.AcquisitionId, acquired.Request, [updatedFile], acquired.Mappings);
            var file = Assert.Single(original.Files);
            var raw = file.Probe.Observations;
            var packets = raw.Packets.Select(p => p.StreamIndex == 0 && p.Pts == 30 ? p with { Position = 0, Size = bytes.Length } : p);
            var observations = new MediaProbeObservations(raw.Streams, raw.Frames, packets, true, true);
            var request = new MediaProbeRequest(acquired.AcquisitionId, owned.Id, owned.Path, identity,
                MediaProbeDetail.FramesAndPackets, file.Probe.Request.Range, true);
            var probe = new MediaProbeResult(request, "fixture", observations, file.Probe.Container);
            var bound = AcquiredMediaInspection.Create(updated, [new(probe, file.Native)]).Data!;
            if (fault == "changed") File.WriteAllBytes(owned.Path, [0, 0, 0, 2, 0x65, 0x89]);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            if (fault == "cancel") stop.Cancel();
            var result = await new ClipRandomAccessInspector().InspectAsync(bound, stop.Token);
            if (fault == "idr")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(30, result.Data!.Value!.Timestamp.Pts);
                Assert.Equal(acquired.AcquisitionId, result.Data.Scope.SnapshotId);
                Assert.Equal(MediaInspectionState.Verified, result.Data.State);
            }
            else if (fault == "cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Data); }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(fault == "changed" ? "ByteIdentityBefore" : "ClosedRandomAccessUnsupported", result.Error!.Field);
            }
            Assert.True(File.Exists(owned.Path));
        }
        finally { File.Delete(owned.Path); Directory.Delete(acquired.Request.WorkDirectory); }
    }
}
