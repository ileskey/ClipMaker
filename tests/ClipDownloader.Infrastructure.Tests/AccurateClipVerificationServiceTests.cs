using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.Services;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class AccurateClipVerificationServiceTests
{
    private sealed class Probe(string fault) : IMediaProbe
    {
        internal int Calls;
        public Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken token)
        {
            Calls++;
            if (fault == "termination") return Task.FromResult(MediaOperationResult<MediaProbeResult>.Failed(new(MediaFailureKind.TerminationUnconfirmed, "safe", "ProbeTermination")));
            if (fault == "cancel") return Task.FromResult(MediaOperationResult<MediaProbeResult>.Cancelled());
            var video = new MediaStreamObservation(0, MediaStreamKind.Video, F.Time(1, 30), fault == "shift" ? 1 : 0, F.Rate(), F.Rate(), 1, new("h264", "High", "yuv420p", null, null, null, 45));
            var audio = new MediaStreamObservation(1, MediaStreamKind.Audio, F.Time(1, 48000), 0, null, null, 2, new("aac", "LC", null, "fltp", 48000, 2, 5));
            var frames = Enumerable.Range(0, 30).Select(i => new MediaFrameObservation(0, i + (fault == "shift" ? 1 : 0), 1, null, null, null, null))
                .Concat(Enumerable.Range(0, 47).Select(i => new MediaFrameObservation(1, i * 1024, i == 46 ? 896 : 1024, 1024, "fltp", 48000, 2)));
            var packets = Enumerable.Range(0, 30).Select(i => new MediaPacketObservation(0, i + (fault == "shift" ? 1 : 0), i + (fault == "shift" ? 1 : 0), 1, i == 0 ? "K_" : "__", null, false, 1, i))
                .Concat(Enumerable.Range(0, 48).Select(i => new MediaPacketObservation(1, i * 1024 - 1024, i * 1024 - 1024, i == 47 ? 896 : 1024,
                    i == 0 ? "KD_" : "K_", i == 0 ? new(1024, 0, 0, 0) : null, false, 1, 30 + i)));
            var samples = Enumerable.Range(0, 30).Select(i => new Mp4NativeSample(1, i, i, 1, 1, i == 0 ? 0u : 0x01010000u, i))
                .Concat(Enumerable.Range(0, 48).Select(i => new Mp4NativeSample(2, i * 1024, i * 1024, i == 47 ? 896u : 1024u, 1, 0, 30 + i)));
            var videoEdits = fault == "shift" ? new Mp4EditObservation[] { new(1600, -1, 1, 0), new(48000, 0, 1, 0) } : [new(48000, 0, 1, 0)];
            var header = new Mp4TimingObservation(48000, [new(1, 30, true, videoEdits, fault == "shift" ? 49600u : 48000u, 30),
                new(2, 48000, true, [new(48000, 1024, 1, 0)], 48000, 49024)], fault == "shift" ? 49600u : 48000u);
            var native = new Mp4NativeSampleEvidence([new(1, "h264", 45, new string('a', 64), null, null), new(2, "aac", 5, new string('b', 64), 48000, 2)], samples);
            return Task.FromResult(MediaOperationResult<MediaProbeResult>.Succeeded(new(request, "fixture", new([video, audio], frames, packets, true, true),
                new(header, [], MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(native)))));
        }
    }
    private static MediaFileContentIdentity Identity(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));

    [Theory]
    [InlineData("success", null)]
    [InlineData("shift", "AccurateVideoMembership")]
    [InlineData("quality", "AccurateDecodedLayout")]
    [InlineData("changed", "AccurateByteIdentityBefore")]
    [InlineData("termination", "ProbeTermination")]
    [InlineData("cancel", null)]
    public async Task ChecksExactMembershipSupportDecodedLayoutAndOwnedIdentities(string fault, string? field)
    {
        var f = ClipProcessingQualifierTests.Fixture(ClipMode.Accurate);
        var old = f.Inspection; var oldFile = old.Acquisition.Files.Single();
        string directory = Path.GetDirectoryName(oldFile.Path)!; Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, "staging.mp4"), renderPath = Path.Combine(directory, "render.f32le");
        var token = TestContext.Current.CancellationToken;
        byte[] inputBytes = new byte[240], decodedBytes = new byte[153600 * 8], renderBytes = new byte[48000 * 8], outputBytes = new byte[78];
        var source = new OwnedMediaFile(oldFile.Id, oldFile.Path, oldFile.SourceFormatIds, Identity(inputBytes));
        await File.WriteAllBytesAsync(source.Path, inputBytes, token);
        await File.WriteAllBytesAsync(f.Preparation.Value!.DecodedPcm.Path, decodedBytes, token);
        await File.WriteAllBytesAsync(renderPath, renderBytes, token); await File.WriteAllBytesAsync(staging, outputBytes, token);
        try
        {
            var acquisition = new AcquiredMedia(old.Acquisition.AcquisitionId, old.Acquisition.Request, [source], old.Acquisition.Mappings);
            var previous = old.Files.Single();
            var request = new MediaProbeRequest(acquisition.AcquisitionId, source.Id, source.Path, source.Content!, MediaProbeDetail.FramesAndPackets, previous.Probe.Request.Range, true);
            var inspection = AcquiredMediaInspection.Create(acquisition, [new(new(request, "fixture", previous.Probe.Observations, previous.Probe.Container),
                previous.Native, previous.SourceStart! with { TargetContent = source.Content! })]).Data!;
            var preparation = f.Preparation.Value with { DecodedPcm = f.Preparation.Value.DecodedPcm with { Content = Identity(decodedBytes) } };
            var plan = new ClipProcessingQualifier().Qualify(inspection, decoderPreparation: MediaInspectionFact<AudioDecoderPreparation>.Verified(preparation, f.Preparation.Scope)).Data!;
            var render = new MediaAudioRenderInput(plan.Processing.AudioBoundary!, preparation.DecodedPcm, new(Guid.NewGuid(), renderPath, Identity(renderBytes)), new(48000, 0, 0), 2);
            var processingRequest = new MediaProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), plan.Processing, staging, render);
            var processing = new MediaProcessingResult(processingRequest, new(processingRequest.OutputFileId, staging, Identity(outputBytes)), "fixture");
            if (fault == "changed") await File.WriteAllBytesAsync(renderPath, new byte[1], token);
            var probe = new Probe(fault);
            var service = new AccurateClipVerificationService(probe, (reference, cancellation) => Task.FromResult(
                OperationResult<DecodedPictureObservations>.Succeeded(new(F.Time(1, 30), fault == "quality" ? 160 : 1920,
                    fault == "quality" ? 90 : 1080, Enumerable.Range(0, 30).Select(i => new DecodedPictureObservation(i, 1, 1, i.ToString("x64")))))));
            var result = await service.VerifyAsync(plan, processing, token);
            if (fault == "success")
            {
                Assert.Equal(OperationStatus.Succeeded, result.Status); Assert.Equal(F.Time(1), result.Data!.VideoSourcePresentation.Start);
                Assert.Equal(F.Time(2), result.Data.VideoSourcePresentation.End); Assert.Equal(F.Time(2), result.Data.AudioSourcePresentation!.End);
                Assert.Equal(1024, result.Data.Output.Streams[1].PrimingSkippedSamples); Assert.Equal(128, result.Data.Output.Streams[1].DecodedTailPaddingSamples);
            }
            else if (fault == "cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.Data); }
            else { Assert.Null(result.Data); Assert.Equal(field, result.Error!.Field); }
            if (fault == "termination")
            {
                Assert.True(service.HasUnconfirmedProcess(processingRequest.OperationId));
                Assert.Equal("PreviousVerificationTermination", (await service.VerifyAsync(plan, processing, token)).Error!.Field); Assert.Equal(1, probe.Calls);
            }
            Assert.True(File.Exists(source.Path)); Assert.True(File.Exists(staging));
        }
        finally { foreach (var path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }
}
