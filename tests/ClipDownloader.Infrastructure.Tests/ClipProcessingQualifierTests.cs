using ClipDownloader.Application.Models;
using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ClipProcessingQualifierTests
{
    internal static (AcquiredMediaInspection Inspection, MediaInspectionFact<MediaRandomAccessPoint> Key,
        MediaInspectionFact<AudioDecoderPreparation> Preparation) Fixture(ClipMode mode, string fault = "", long shift = 0)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "qualifier-tests", Guid.NewGuid().ToString("N"));
        VideoSourceReference.TryCreate(VideoPlatform.Chzzk, "123", out var source);
        var format = new VideoFormat("v", 1920, 1080, F.Rate(), "h264", "aac", StreamPresence.Present, StreamPresence.Present);
        var info = new VideoInfo(source!, null, TimeSpan.FromSeconds(120), null, null, null, VideoState.Completed, [format], []);
        var selected = FormatSelection.Create(info, QualityOption.Best, format, null, FormatSelectionReason.RequestedBest).Data!;
        var selection = SelectedSourceSnapshot.Create(Guid.NewGuid(), info, selected, mode).Data!;
        var content = new MediaFileContentIdentity(16, new string('a', 64));
        var initialFile = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(directory, "initial.mp4"), ["v"], content);
        var targetFile = new OwnedMediaFile(Guid.NewGuid(), Path.Combine(directory, "target.mp4"), ["v"], content);
        Guid initialId = Guid.NewGuid(), acquisitionId = Guid.NewGuid();
        var initialScope = new MediaObservationScope(initialId, new(initialFile.Id, 0, MediaStreamKind.Video),
            MediaInspectionKind.Summary, MediaTimeBasis.Native, null, ObservationCompleteness.Complete);
        var offset = F.Time(shift);
        var sourceOrigin = fault == "clock" ? F.Time(1, (long)int.MaxValue + 1) : offset;
        var origin = fault == "origin" ? MediaInspectionFact<SignedMediaTime>.Unknown(initialScope, MediaInspectionIssue.NotInspected) :
            MediaInspectionFact<SignedMediaTime>.Verified(sourceOrigin, initialScope);
        var axis = fault == "axis" ? MediaInspectionFact<SourceAxisRelationship>.Unknown(initialScope, MediaInspectionIssue.NotInspected) :
            MediaInspectionFact<SourceAxisRelationship>.Verified(new("v", null, SignedMediaTime.Zero), initialScope);
        var timeline = new SourceTimelineDescriptor(origin, axis,
            MediaInspectionFact<SignedMediaTime>.Unknown(initialScope, MediaInspectionIssue.NotInspected),
            fault == "presence" ? StreamPresence.Unknown : StreamPresence.Present);
        var inspectionRequest = new SelectedSourceInspectionRequest(Guid.NewGuid(), selection, SourceInspectionPurpose.SourceTimeline,
            directory, new(1024, F.Time(10)));
        var initial = new SelectedSourceInspection(initialId, inspectionRequest, [initialFile],
            MediaInspectionFact<FrameRate>.Verified(F.Rate(), initialScope), timeline);
        var acquisitionRequest = new AcquisitionRequest(inspectionRequest.JobId, selection, initial, F.UserRange(), directory);
        PresentationRange.TryCreate(F.Time(1) + sourceOrigin, (fault == "coverage" ? F.Time(3, 2) : F.Time(2)) + sourceOrigin, out var coverage, out _);
        var videoReference = new MediaStreamReference(targetFile.Id, 0, MediaStreamKind.Video);
        var audioReference = new MediaStreamReference(targetFile.Id, 1, MediaStreamKind.Audio);
        MediaObservationScope Scope(MediaStreamReference stream, MediaInspectionKind kind, PresentationRange range) =>
            new(acquisitionId, stream, kind, MediaTimeBasis.Native, range, ObservationCompleteness.Complete);
        var videoScope = Scope(videoReference, MediaInspectionKind.Frames, coverage!);
        var audioScope = Scope(audioReference, MediaInspectionKind.Samples, coverage!);
        PresentationRange.TryCreate(offset, F.Time(3) + offset, out var fullVideo, out _);
        PresentationRange.TryCreate(offset, F.Time(16, 5) + offset, out var fullAudio, out _);
        var mappings = new[] { new SourceStreamMapping(MediaInspectionFact<SignedMediaTime>.Verified(offset, Scope(videoReference, MediaInspectionKind.Frames,
            fault == "mapping-scope" ? coverage! : fullVideo!)),
            MediaInspectionFact<PresentationRange>.Verified(coverage!, videoScope)),
            new SourceStreamMapping(MediaInspectionFact<SignedMediaTime>.Verified(offset, Scope(audioReference, MediaInspectionKind.Samples, fullAudio!)),
            MediaInspectionFact<PresentationRange>.Verified(coverage!, audioScope)) };
        var acquired = new AcquiredMedia(acquisitionId, acquisitionRequest, [targetFile], mappings);
        var video = new MediaStreamObservation(0, MediaStreamKind.Video, F.Time(1, 30), 0, F.Rate(), F.Rate(), 1,
            new("h264", "High", "yuv420p", null, null, null, 45));
        var audio = new MediaStreamObservation(1, MediaStreamKind.Audio, F.Time(1, 48000), 0, null, null, 2,
            new("aac", "LC", null, "fltp", 48000, 2, 2));
        var frames = Enumerable.Range(0, 90).Select(i => new MediaFrameObservation(0, i, 1, null, null, null, null))
            .Concat(Enumerable.Range(0, 150).Select(i => new MediaFrameObservation(1, i * 1024, 1024, 1024, "fltp", 48000, 2))).ToArray();
        var packets = Enumerable.Range(0, 90).Select(i => new MediaPacketObservation(0, i, i, 1, i % 30 == 0 ? "K_" : "__", null, false, 1, i))
            .Concat(Enumerable.Range(0, 150).Select(i => new MediaPacketObservation(1, i * 1024, i * 1024, 1024, "K_", null, false, 1, 90 + i))).ToArray();
        var raw = new MediaProbeObservations([video, audio], frames, packets, true, true);
        var container = new Mp4FileTimingEvidence(new(48000, [new(1, 30, false, []), new(2, 48000, false, [])]), [],
            MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(new([], [])));
        var probeRequest = new MediaProbeRequest(acquisitionId, targetFile.Id, targetFile.Path, content,
            MediaProbeDetail.FramesAndPackets, coverage!, true);
        var probe = new MediaProbeResult(probeRequest, "fixture", raw, container);
        PresentationRange.TryCreate(F.Time(0), F.Time(16, 5), out var audioRange, out _);
        var native = new Mp4NativeObservation([new(video, new(1, "h264", 45, new string('a', 64), null, null), F.Range(0, 3)),
            new(audio, new(2, "aac", 2, new string('b', 64), 48000, 2), audioRange!)]);
        var prefix = new SourceStartPrefixEvidence(initialFile.Id, content, targetFile.Id, content, content.Sha256);
        var bound = AcquiredMediaInspection.Create(acquired, [new(probe, native, fault == "prefix" ? null : prefix)]).Data!;
        NativeTimestamp.TryCreate(fault == "late-key" ? 60 : fault == "mapping-scope" ? 0 : 30, F.Time(1, 30), out var key, out _);
        PresentationRange.TryCreate(key!.PresentationTime, key.PresentationTime + F.Time(1, 30), out var keyRange, out _);
        var keyFact = MediaInspectionFact<MediaRandomAccessPoint>.Verified(new(key, new string('c', 64)),
            Scope(videoReference, MediaInspectionKind.Frames, keyRange!));
        var decoded = new MediaInputFile(Guid.NewGuid(), Path.Combine(directory, "decoded.f32le"), new(153600 * 2 * 4, new string('d', 64)));
        var preparation = MediaInspectionFact<AudioDecoderPreparation>.Verified(new(initialFile.Id, content, decoded, 48000, 2,
            fault == "pcm" ? 1 : 153600), audioScope);
        return (bound, keyFact, preparation);
    }

    [Theory]
    [InlineData("origin", "OriginUnverified")]
    [InlineData("axis", "SharedAxisUnverified")]
    [InlineData("presence", "AudioPresenceUnverified")]
    [InlineData("late-key", "KeyframeAfterIn")]
    [InlineData("clock", "MovieClockUnsupported")]
    [InlineData("coverage", "CoverageIncomplete")]
    [InlineData("mapping-scope", "KeyframeMappingUnverified")]
    public void UnknownOrUnsupportedDependenciesDoNotQualify(string fault, string field)
    {
        var input = Fixture(ClipMode.Fast, fault);
        var result = new ClipProcessingQualifier().Qualify(input.Inspection, input.Key);
        Assert.Null(result.Data); Assert.Equal(field, result.Error!.Field);
    }

    [Fact]
    public void FastUsesOneRenderOriginAndIsInvariantUnderSourceTranslation()
    {
        var original = Fixture(ClipMode.Fast); var moved = Fixture(ClipMode.Fast, shift: 7);
        var qualifier = new ClipProcessingQualifier();
        var first = qualifier.Qualify(original.Inspection, original.Key).Data!;
        var second = qualifier.Qualify(moved.Inspection, moved.Key).Data!;
        Assert.Equal(F.Time(1), first.RenderOrigin); Assert.Equal(F.Time(8), second.RenderOrigin);
        Assert.Equal(first.Processing.Streams.Select(s => s.Translation), second.Processing.Streams.Select(s => s.Translation));
        Assert.Equal(new[] { F.Time(-1), F.Time(-1) }, first.Processing.Streams.Select(s => s.Translation));
        Assert.Equal(30, first.Processing.RequiredVideoFrames.Count);
        Assert.Null(first.Processing.AudioBoundary);
        Assert.Equal("KeyframeUnverified", qualifier.Qualify(original.Inspection).Error!.Field);
    }

    [Fact]
    public void AccurateRequiresScopedExecutedPreparationAndKeepsAudioRequestedCeilGrid()
    {
        var input = Fixture(ClipMode.Accurate); var qualifier = new ClipProcessingQualifier();
        Assert.Equal("DecoderPreparationUnverified", qualifier.Qualify(input.Inspection).Error!.Field);
        var result = qualifier.Qualify(input.Inspection, decoderPreparation: input.Preparation);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var boundary = result.Data!.Processing.AudioBoundary!;
        Assert.Equal(48000, boundary.RequestedFirst); Assert.Equal(96000, boundary.RequestedEnd);
        Assert.Equal(F.Time(1), boundary.RenderOrigin); Assert.Equal(SignedMediaTime.Zero, boundary.Residual);
        var broken = Fixture(ClipMode.Accurate, "pcm");
        Assert.Equal("DecoderPreparationIdentity", qualifier.Qualify(broken.Inspection, decoderPreparation: broken.Preparation).Error!.Field);
        var unboundPrefix = Fixture(ClipMode.Accurate, "prefix");
        Assert.Equal("DecoderPreparationIdentity", qualifier.Qualify(unboundPrefix.Inspection, decoderPreparation: unboundPrefix.Preparation).Error!.Field);
    }

    [Fact]
    public void PartialSupportPreservesRequestedRenderOriginInsteadOfAvailableFirstSample()
    {
        var lattice = new AudioSampleLattice(F.Time(1, 10), 48000);
        var boundary = new AudioBoundaryPlan(lattice, 100, 1000, 200, 800, lattice.GetSampleTime(100) - F.Time(1, 96000));
        Assert.Equal(200, boundary.SelectedFirst); Assert.Equal(800, boundary.SelectedEnd);
        Assert.Equal(lattice.GetSampleTime(100), boundary.RenderOrigin);
        Assert.Equal(F.Time(1, 96000), boundary.Residual);
    }
}
