using ClipMaker.Media.Core.Models;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class PresentationPayloadBridgeTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("timestamp")]
    [InlineData("payload")]
    [InlineData("edit")]
    [InlineData("missing-proof")]
    [InlineData("changed")]
    public async Task EditedAacRequiresCodedPayloadClockAndProvenSkipRatherThanAnAssumedAudibleOffset(string fault)
    {
        string directory = Path.Combine(Path.GetTempPath(), "edited-aac-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "source.mp4");
        byte[] first = [1, 2, 3], second = [4, 5, 6, 7], third = [8, 9, 10], fourth = [11, 12, 13, 14];
        byte[] bytes = first.Concat(second).Concat(third).Concat(fourth).ToArray(); File.WriteAllBytes(path, bytes);
        try
        {
            var owned = new OwnedMediaFile(Guid.NewGuid(), path, ["a"]);
            var config = new Mp4TrackConfiguration(1, "aac", 2, new string('a', 64), 44100, 2);
            var stream = new InitialStreamObservation(0, MediaStreamKind.Audio, F.Time(1, 44100), 0, null, null, 1,
                new("aac", "LC", null, "fltp", 44100, 2, 2));
            var packets = new InitialPacketObservation[] { new(0, -1600, -1600, 1024, "K__", new(1600, 0, 0, 0), false, 3, 0),
                new(0, -576, -576, 1024, "K__", null, false, 4, 3), new(0, 448, 448, 1024, "K__", null, false, 3, 7),
                new(0, 1472, 1472, 1024, "K__", null, false, 4, 10) };
            var samples = new Mp4NativeSample[] { new(1, 0, 0, 1024, 3, 0, 0), new(1, 1024, 1024, 1024, 4, 0, 3),
                new(1, 2048, 2048, 1024, 3, 0, 7), new(1, 3072, 3072, 1024, 4, 0, 10) };
            var track = new Mp4TrackTiming(1, 44100, true, [new(44100, 1600, 1, 0)]);
            var reference = new ProbedInitialReference(new IndexedCollectedReference(owned, bytes.Length, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                F.Range(0, 1), F.Range(0, 1)), "fixture", new([stream], [new(0, 0, 448, 448, "fltp", 44100, 2),
                    new(0, 448, 1024, 1024, "fltp", 44100, 2), new(0, 1472, 1024, 1024, "fltp", 44100, 2)], packets, true),
                new(44100, [track]), [new(0, track)], [], OperationResult<Mp4NativeSampleEvidence>.Succeeded(new([config], samples)));
            var verified = new HlsNativeReferenceVerifier().Verify(reference); Assert.Equal(OperationStatus.Succeeded, verified.Status);
            var source = verified.Data!;
            if (fault == "edit") source = new(reference with { ContainerTiming = new(44100, [track with { Edits = [new(44100, 1599, 1, 0)] }]) }, source.Streams);
            if (fault == "missing-proof") source = new(reference, [source.Streams[0] with { LeadingSkippedSamples = 0 }]);
            var frames = new PackedAacFrame[] { new(Convert.ToHexString(SHA256.HashData(third)), 44100, 2, 1024),
                new(Convert.ToHexString(SHA256.HashData(fault == "payload" ? first : fourth)), 44100, 2, 1024) };
            if (fault == "changed") File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray());
            var result = await new PresentationPayloadBridge().VerifyAudioAsync(source, new(fault == "timestamp" ? 915u : 914u, frames), TestContext.Current.CancellationToken);
            Assert.Equal(fault == "success" ? OperationStatus.Succeeded : OperationStatus.Failed, result.Status);
            if (fault == "success") Assert.Equal(new PresentationPayloadEvidence(2, 1, 0, 2), result.Data); else Assert.Null(result.Data);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
    [Theory]
    [InlineData("success")]
    [InlineData("timestamp")]
    [InlineData("payload")]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("cancel")]
    public async Task PayloadClockAndSameFileIdentityMustAllMatch(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "presentation-payload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "source.mp4");
        byte[] first = [0, 0, 0, 2, 0x65, 1], second = [0, 0, 0, 2, 0x65, 2];
        byte[] bytes = first.Concat(second).ToArray(); File.WriteAllBytes(path, bytes);
        try
        {
            var file = new OwnedMediaFile(Guid.NewGuid(), path, ["v"]);
            var original = HlsNativeReferenceVerifierTests.Reference(file);
            var native = original.NativeSamples!.Data!;
            var samples = native.Samples.Select((s, i) => s with { Size = 6, ByteOffset = i * 6 }).ToArray();
            var reference = original with
            {
                Reference = new IndexedCollectedReference(file, bytes.Length, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), F.Range(0, 2), F.Range(0, 2)),
                NativeSamples = OperationResult<Mp4NativeSampleEvidence>.Succeeded(new(native.Configurations, samples))
            };
            var source = new VerifiedHlsNativeReference(reference,
                [new(reference.Observations.Streams.Single(), native.Configurations.Single(), F.Range(2, 3))]);
            var normalizer = new H264PicturePayloadReader();
            var pictures = new H264CarrierPicture[]
            {
                new(fault == "missing" ? null : 180000, normalizer.Read(first, false).Data!),
                new(fault == "timestamp" ? 181501 : 181500, normalizer.Read(fault == "payload" ? first : second, false).Data!)
            };
            if (fault == "changed") File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray());
            var result = await new PresentationPayloadBridge().VerifyVideoAsync(source, new(F.Time(1, 90000), pictures),
                fault == "cancel" ? new(true) : TestContext.Current.CancellationToken);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(new PresentationPayloadEvidence(2, 2, 0), result.Data);
            }
            else
            {
                Assert.Null(result.Data);
                Assert.Equal(fault == "cancel" ? OperationStatus.Cancelled : OperationStatus.Failed, result.Status);
            }
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
