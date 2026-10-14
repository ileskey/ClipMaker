using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class FastClipVerificationServiceTests
{
    [Theory]
    [InlineData(1025, true, true, 1025)]
    [InlineData(1023, true, true, 1023)]
    [InlineData(2048, true, true, 2048)]
    [InlineData(1025, false, true, 1024)]
    [InlineData(1025, true, false, 1024)]
    public void CodedAacDurationUsesExactNextSourceDtsAndFinalPacketKeepsItsDuration(long distance, bool audio, bool hasNext, long expected)
    {
        var packet = new MediaPacketObservation(1, 100, 100, 1024, "K_", null, false, 1, 0);
        var next = hasNext ? packet with { Pts = 100 + distance, Dts = 100 + distance } : null;
        Assert.Equal(F.Time(expected, 48000), FastClipVerificationService.ExpectedCopyDuration(packet, next, audio, F.Time(1, 48000)));
        Assert.Null(FastClipVerificationService.ExpectedCopyDuration(packet, packet, true, F.Time(1, 48000)));
    }

    private sealed class Probe(Func<MediaProbeRequest, MediaOperationResult<MediaProbeResult>> result) : IMediaProbe
    {
        internal int Calls;
        public Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken token)
        { Calls++; return Task.FromResult(result(request)); }
    }

    private static MediaFileContentIdentity Identity(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));

    [Theory]
    [InlineData("success", null)]
    [InlineData("payload", "CopyPacketPayloadChanged")]
    [InlineData("duration", "CopyPacketChanged")]
    [InlineData("picture", "CopyDecodedPictureChanged")]
    [InlineData("quality", "CopyDecodedPictureLayout")]
    [InlineData("input", "VerificationByteIdentityBefore")]
    [InlineData("termination", "ProbeTermination")]
    [InlineData("decode-termination", "DecodeTermination")]
    [InlineData("cancel", null)]
    public async Task VerificationChecksContentTimingPicturesIdentityAndTermination(string fault, string? expectedField)
    {
        var fixture = ClipProcessingQualifierTests.Fixture(ClipMode.Fast);
        var old = fixture.Inspection;
        var testToken = TestContext.Current.CancellationToken;
        string directory = Path.GetDirectoryName(old.Acquisition.Files.Single().Path)!;
        Directory.CreateDirectory(directory);
        byte[] sourceBytes = Enumerable.Range(0, 240).Select(i => (byte)i).ToArray();
        var sourceContent = Identity(sourceBytes);
        var original = old.Acquisition.Files.Single();
        var source = new OwnedMediaFile(original.Id, original.Path, original.SourceFormatIds, sourceContent);
        await File.WriteAllBytesAsync(source.Path, sourceBytes, testToken);
        string staging = Path.Combine(directory, "output.mp4");
        try
        {
            var acquisition = new AcquiredMedia(old.Acquisition.AcquisitionId, old.Acquisition.Request, [source], old.Acquisition.Mappings);
            var oldFile = old.Files.Single();
            var inputRequest = new MediaProbeRequest(acquisition.AcquisitionId, source.Id, source.Path, sourceContent,
                MediaProbeDetail.FramesAndPackets, oldFile.Probe.Request.Range, true);
            var inputProbe = new MediaProbeResult(inputRequest, "fixture", oldFile.Probe.Observations, oldFile.Probe.Container);
            var inspection = AcquiredMediaInspection.Create(acquisition, [new(inputProbe, oldFile.Native)]).Data!;
            var plan = new ClipProcessingQualifier().Qualify(inspection, fixture.Key).Data!;
            byte[] outputBytes = sourceBytes.Skip(30).Take(30).Concat(sourceBytes.Skip(136).Take(48)).ToArray();
            if (fault == "payload") outputBytes[0] ^= 1;
            await File.WriteAllBytesAsync(staging, outputBytes, testToken);
            var processingRequest = new MediaProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), plan.Processing, staging);
            var processing = new MediaProcessingResult(processingRequest,
                new(processingRequest.OutputFileId, staging, Identity(outputBytes)), "fixture");
            if (fault == "input") { sourceBytes[0] ^= 1; await File.WriteAllBytesAsync(source.Path, sourceBytes, testToken); }
            var probe = new Probe(request => fault == "termination"
                ? MediaOperationResult<MediaProbeResult>.Failed(new(MediaFailureKind.TerminationUnconfirmed, "safe", "ProbeTermination"))
                : fault == "cancel" ? MediaOperationResult<MediaProbeResult>.Cancelled()
                : MediaOperationResult<MediaProbeResult>.Succeeded(Output(request, fault)));
            var service = new FastClipVerificationService(probe, (reference, token) =>
            {
                if (fault == "decode-termination") return Task.FromResult(OperationResult<DecodedPictureObservations>.Failed(
                    new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "safe", "DecodeTermination")));
                bool output = reference.File.Id == processing.Staging.Id;
                int count = output ? 30 : 90;
                var pictures = Enumerable.Range(0, count).Select(i => new DecodedPictureObservation(i, 1, 1920 * 1080 * 3 / 2,
                    (i + (output ? 30 : 0) + (fault == "picture" && output && i == 0 ? 1 : 0)).ToString("x64")));
                return Task.FromResult(OperationResult<DecodedPictureObservations>.Succeeded(new(F.Time(1, 30),
                    fault == "quality" ? 160 : 1920, fault == "quality" ? 90 : 1080, pictures)));
            });
            var result = await service.VerifyAsync(plan, processing, testToken);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status);
                Assert.Equal(F.Time(1), result.Data!.VideoSourcePresentation.Start);
                Assert.Equal(F.Time(2), result.Data.VideoSourcePresentation.End);
            }
            else if (fault == "cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Data); }
            else { Assert.Null(result.Data); Assert.Equal(expectedField, result.Error!.Field); }
            if (fault is "termination" or "decode-termination")
            {
                Assert.True(service.HasUnconfirmedProcess(processingRequest.OperationId));
                Assert.Equal("PreviousVerificationTermination", (await service.VerifyAsync(plan, processing, testToken)).Error!.Field);
                Assert.Equal(1, probe.Calls);
            }
            Assert.True(File.Exists(source.Path)); Assert.True(File.Exists(staging));
        }
        finally { File.Delete(source.Path); File.Delete(staging); Directory.Delete(directory); }
    }

    private static MediaProbeResult Output(MediaProbeRequest request, string fault)
    {
        var video = new MediaStreamObservation(0, MediaStreamKind.Video, F.Time(1, 30), 0, F.Rate(), F.Rate(), 1,
            new("h264", "High", "yuv420p", null, null, null, 45));
        var audio = new MediaStreamObservation(1, MediaStreamKind.Audio, F.Time(1, 48000), 0, null, null, 2,
            new("aac", "LC", null, "fltp", 48000, 2, 2));
        var frames = Enumerable.Range(0, 30).Select(i => new MediaFrameObservation(0, i, 1, null, null, null, null))
            .Concat(Enumerable.Range(0, 48).Select(i => new MediaFrameObservation(1, i == 0 ? 0 : i * 1024 - 896,
                1024, i == 0 ? 128 : 1024, "fltp", 48000, 2))).ToArray();
        var packets = Enumerable.Range(0, 30).Select(i => new MediaPacketObservation(0, i, i, 1,
            i == 0 ? "K_" : "__", null, false, 1, i))
            .Concat(Enumerable.Range(0, 48).Select(i => new MediaPacketObservation(1, i * 1024 - 896, i * 1024 - 896,
                fault == "duration" && i == 47 ? 1023 : 1024, "K_", i == 0 ? new(896, 0, 0, 0) : null, false, 1, 30 + i))).ToArray();
        uint audioEnd = fault == "duration" ? 48255u : 48256u;
        var samples = Enumerable.Range(0, 30).Select(i => new Mp4NativeSample(1, i, i, 1, 1, i == 0 ? 0u : 0x01010000u, i))
            .Concat(Enumerable.Range(0, 48).Select(i => new Mp4NativeSample(2, i * 1024, i * 1024,
                fault == "duration" && i == 47 ? 1023u : 1024u, 1, 0, 30 + i))).ToArray();
        var header = new Mp4TimingObservation(48000, [new(1, 30, true, [new(48000, 0, 1, 0)], 48000, 30),
            new(2, 48000, true, [new(audioEnd, 896, 1, 0)], audioEnd, audioEnd + 896)], audioEnd);
        var native = new Mp4NativeSampleEvidence([new(1, "h264", 45, new string('a', 64), null, null),
            new(2, "aac", 2, new string('b', 64), 48000, 2)], samples);
        return new(request, "fixture", new([video, audio], frames, packets, true, true),
            new(header, [], MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(native)));
    }
}
