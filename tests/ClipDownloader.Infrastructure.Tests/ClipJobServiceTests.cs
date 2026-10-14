using System.Security.Cryptography;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.UseCases;
using ClipDownloader.Application.Validation;
using ClipDownloader.Infrastructure.FileSystem;
using ClipDownloader.Infrastructure.Sources;
using ClipMaker.Media.Core.Models;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ClipJobServiceTests
{
    private sealed class Diagnostics(bool omitFeatures = false) : IToolExecutionDiagnostics
    {
        public Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(ToolDiagnosticRequest request, CancellationToken token) => Task.FromResult(
            OperationResult<ToolExecutionReport>.Succeeded(new(request.Command, Enum.GetValues<ToolKind>().Select(kind => new ToolExecutionDiagnostic(kind,
                new(kind.ToString(), ToolDiagnosticStatus.Found, false, "local", ToolDiagnosticSource.Path, [], null), ToolCheckStatus.Passed,
                ToolCheckStatus.Passed, "fixture", 0, null, kind == ToolKind.Ffmpeg && !omitFeatures ? request.RequiredFeatures.Select(f => new ToolFeatureCheck(f, ToolCheckStatus.Passed, null)) : [])))));
    }
    private sealed class Metadata(VideoInfo info) : IVideoInfoService
    {
        internal VideoInfo Info = info;
        internal int Calls;
        public Task<OperationResult<VideoInfo>> GetVideoInfoAsync(Uri url, CancellationToken token) { Calls++; return Task.FromResult(OperationResult<VideoInfo>.Succeeded(Info)); }
    }
    private sealed class Workspace(IOutputWorkspace inner, string? cancelledRemaining = null) : IOutputWorkspace
    {
        internal IOutputSession? Session; internal int Calls;
        public async Task<OperationResult<IOutputSession>> CreateAsync(Guid job, string path, CancellationToken token)
        { Calls++; if (cancelledRemaining is not null) return OperationResult<IOutputSession>.Cancelled(["partial cleanup"], [cancelledRemaining]);
            var result = await inner.CreateAsync(job, path, token); Session = result.Data; return result; }
    }
    private sealed class Pipeline(string fault, CancellationTokenSource stop) : ISelectedSourceInspectionService, IClipDownloadService, ISourceResourceAccess, IClipMediaService
    {
        private readonly Dictionary<Guid, List<OwnedMediaFile>> source = [];
        private readonly Dictionary<Guid, List<MediaOwnedProcessingFile>> outputs = [];
        internal int Inspections, Acquisitions, Processing;
        internal FileStream? Held;
        private static MediaFileContentIdentity Identity(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        public SourceOwnedResources GetOwnedResources(Guid id) => new(id, source.GetValueOrDefault(id) ?? [], false, false);
        ClipMediaResources IClipMediaService.GetOwnedResources(Guid id) => new(outputs.GetValueOrDefault(id) ?? [], fault == "termination", fault == "handle");
        public async Task<OperationResult<SelectedSourceInspection>> InspectAsync(SelectedSourceInspectionRequest request, CancellationToken token)
        {
            Inspections++;
            string path = Path.Combine(request.WorkDirectory, "initial.mp4"); byte[] bytes = [1, 2, 3];
            await File.WriteAllBytesAsync(path, bytes, token);
            var file = new OwnedMediaFile(Guid.NewGuid(), path, [request.Selection.Selection.VideoFormat.Id], Identity(bytes));
            source[request.JobId] = [file]; Guid id = Guid.NewGuid();
            var scope = new MediaObservationScope(id, new(file.Id, 0, MediaStreamKind.Video), MediaInspectionKind.Summary, MediaTimeBasis.Native, null, ObservationCompleteness.Complete);
            var timeline = new SourceTimelineDescriptor(MediaInspectionFact<ClipMaker.Media.Core.ValueObjects.SignedMediaTime>.Verified(F.Time(0), scope),
                MediaInspectionFact<SourceAxisRelationship>.Verified(new(request.Selection.Selection.VideoFormat.Id, null, F.Time(0)), scope),
                MediaInspectionFact<ClipMaker.Media.Core.ValueObjects.SignedMediaTime>.Unknown(scope, MediaInspectionIssue.NotInspected), StreamPresence.Present);
            return OperationResult<SelectedSourceInspection>.Succeeded(new(id, request, [file], MediaInspectionFact<ClipMaker.Media.Core.ValueObjects.FrameRate>.Verified(F.Rate(), scope), timeline));
        }
        public async Task<OperationResult<AcquiredMedia>> AcquireAsync(AcquisitionRequest request, IProgress<AcquisitionProgress>? progress, CancellationToken token)
        {
            Acquisitions++;
            byte[] bytes = [3, 4, 5]; string path = Path.Combine(request.WorkDirectory, "source.mp4");
            await File.WriteAllBytesAsync(path, bytes, token);
            var file = new OwnedMediaFile(Guid.NewGuid(), path, [request.Selection.Selection.VideoFormat.Id], Identity(bytes)); source[request.JobId].Add(file);
            Guid id = Guid.NewGuid();
            SourceStreamMapping Mapping(int index, MediaStreamKind kind, MediaInspectionKind inspection, ClipMaker.Media.Core.ValueObjects.PresentationRange full)
            {
                var stream = new MediaStreamReference(file.Id, index, kind);
                return new(MediaInspectionFact<ClipMaker.Media.Core.ValueObjects.SignedMediaTime>.Verified(F.Time(0), new(id, stream, inspection, MediaTimeBasis.Native, full, ObservationCompleteness.Complete)),
                    MediaInspectionFact<ClipMaker.Media.Core.ValueObjects.PresentationRange>.Verified(F.Range(1, 2), new(id, stream, inspection, MediaTimeBasis.Native, F.Range(1, 2), ObservationCompleteness.Complete)));
            }
            return OperationResult<AcquiredMedia>.Succeeded(new(id, request, [file], [Mapping(0, MediaStreamKind.Video, MediaInspectionKind.Frames, F.Range(0, 3)),
                Mapping(1, MediaStreamKind.Audio, MediaInspectionKind.Samples, F.Range(0, 4))]));
        }
        public async Task<OperationResult<VerifiedClipOutput>> ProcessAsync(AcquiredMedia acquired, Guid stagingId, string stagingPath, IProgress<JobProgress>? progress, CancellationToken token)
        {
            Processing++;
            var original = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection.Files.Single(); var file = acquired.Files.Single();
            var probe = new MediaProbeResult(new(acquired.AcquisitionId, file.Id, file.Path, file.Content!, MediaProbeDetail.FramesAndPackets, F.Range(1, 2), true),
                "fixture", original.Probe.Observations, original.Probe.Container);
            var inspected = AcquiredMediaInspection.Create(acquired, [new(probe, original.Native)]).Data!;
            ClipMaker.Media.Core.ValueObjects.NativeTimestamp.TryCreate(30, F.Time(1, 30), out var timestamp, out _);
            var key = MediaInspectionFact<MediaRandomAccessPoint>.Verified(new(timestamp!, new string('a', 64)),
                new(acquired.AcquisitionId, new(file.Id, 0, MediaStreamKind.Video), MediaInspectionKind.Frames, MediaTimeBasis.Native, F.Range(1, 2), ObservationCompleteness.Complete));
            var plan = new ClipProcessingQualifier().Qualify(inspected, key).Data!;
            byte[] bytes = [6, 7, 8]; await File.WriteAllBytesAsync(stagingPath, bytes, token);
            var staging = new MediaOwnedProcessingFile(stagingId, stagingPath, Identity(bytes)); outputs[acquired.Request.JobId] = [staging];
            if (fault == "termination") { stop.Cancel(); return OperationResult<VerifiedClipOutput>.Failed(new(OperationErrorCode.ProcessTerminationFailed, OperationErrorCategory.Execution, "safe")); }
            if (fault == "handle") { stop.Cancel(); return OperationResult<VerifiedClipOutput>.Cancelled(); }
            if (fault == "media-fail") return OperationResult<VerifiedClipOutput>.Failed(new(OperationErrorCode.VerificationFailed, OperationErrorCategory.Execution, "safe"));
            var request = new MediaProcessingRequest(Guid.NewGuid(), stagingId, plan.Processing, stagingPath);
            var processed = new MediaProcessingResult(request, staging, "fixture");
            var outputProbe = new MediaProbeResult(new(Guid.NewGuid(), stagingId, stagingPath, staging.Content!, MediaProbeDetail.FramesAndPackets, F.Range(0, 1), true, MediaProbePurpose.Output), "fixture", original.Probe.Observations, original.Probe.Container);
            var output = new Mp4OutputObservation(outputProbe, original.Native.Streams.Select(s => new Mp4OutputStreamObservation(s.Stream, s.Configuration, F.Range(0, 1), [], 0, 0)));
            var verified = new VerifiedClipOutput(plan, processed, output, F.Range(1, 2), F.Range(1, 2));
            if (fault == "before-finalize") stop.Cancel();
            if (fault == "cleanup-lock") Held = new(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return OperationResult<VerifiedClipOutput>.Succeeded(verified);
        }
    }
    private sealed class Progress(Action<JobProgress> action) : IProgress<JobProgress> { public void Report(JobProgress value) => action(value); }

    [Theory]
    [InlineData("source", OperationErrorCode.InvalidInput)]
    [InlineData("syntax", OperationErrorCode.InvalidInput)]
    [InlineData("minute", OperationErrorCode.InvalidRange)]
    [InlineData("frames", OperationErrorCode.InvalidRange)]
    [InlineData("equal", OperationErrorCode.InvalidRange)]
    [InlineData("conflict", OperationErrorCode.OutputConflict)]
    [InlineData("live", OperationErrorCode.SourceUnavailable)]
    [InlineData("duration", OperationErrorCode.InvalidMetadata)]
    [InlineData("features", OperationErrorCode.UnsupportedFormat)]
    public async Task InvalidPreparationCannotCreateWorkspaceOrStartSourceOperations(string fault, OperationErrorCode expected)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "prepare-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var selection = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection.Acquisition.Request.Selection;
        var info = new VideoInfo(selection.Source, null, fault == "duration" ? null : TimeSpan.FromSeconds(120), null, null, null,
            fault == "live" ? VideoState.Live : VideoState.Completed, [selection.Selection.VideoFormat], []);
        string final = Path.Combine(directory, "clip.mp4"), root = Path.Combine(directory, "jobs");
        try
        {
            if (fault == "conflict") await File.WriteAllTextAsync(final, "user", stop.Token);
            var validator = new OutputPathValidator(directory); var workspace = new Workspace(new OutputWorkspace(validator, root));
            var pipeline = new Pipeline("", stop);
            var service = new ClipJobService(new Metadata(info), new SupportedVideoSourceParser(), new Diagnostics(fault == "features"), validator,
                workspace, pipeline, pipeline, pipeline, pipeline, new(1024, F.Time(10)));
            string start = fault switch { "syntax" => "bad", "minute" => "00:60:00:00", "frames" => "00:00:01:30", _ => "00:00:01:00" };
            var request = new ClipRequest(fault == "source" ? new Uri("https://chzzk.naver.com/video/999") : info.Source.PageUrl,
                start, fault == "equal" ? start : "00:00:02:00", final);
            var result = await service.PrepareAsync(request, info, stop.Token);
            Assert.Null(result.Data); Assert.Equal(expected, result.Error!.Code);
            Assert.Equal(0, workspace.Calls); Assert.Equal(0, pipeline.Inspections); Assert.False(Directory.Exists(root));
            if (fault == "conflict") Assert.Equal("user", await File.ReadAllTextAsync(final, stop.Token));
        }
        finally { File.Delete(final); Directory.Delete(directory); }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("before-finalize")]
    [InlineData("after-finalize")]
    [InlineData("collision-final")]
    [InlineData("termination")]
    [InlineData("handle")]
    [InlineData("media-fail")]
    [InlineData("cleanup-lock")]
    [InlineData("fps-changed")]
    [InlineData("fps-unknown")]
    [InlineData("workspace-cancel")]
    public async Task PreparationWritesNothingAndExecutionKeepsFinalizationAndCleanupSemantics(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "job-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fixture = ClipProcessingQualifierTests.Fixture(ClipMode.Fast).Inspection.Acquisition.Request.Selection;
        var format = fixture.Selection.VideoFormat with { FrameRate = fault == "fps-unknown" ? null : F.Rate() };
        var info = new VideoInfo(fixture.Source, null, TimeSpan.FromSeconds(120), null, null, null, VideoState.Completed, [format], []);
        var metadata = new Metadata(info); var validator = new OutputPathValidator(directory);
        string workRoot = Path.Combine(directory, "jobs"); var workspace = new Workspace(new OutputWorkspace(validator, workRoot), fault == "workspace-cancel" ? workRoot : null);
        var pipeline = new Pipeline(fault, stop);
        var service = new ClipJobService(metadata, new SupportedVideoSourceParser(), new Diagnostics(), validator, workspace, pipeline, pipeline, pipeline, pipeline, new(1024, F.Time(10)));
        string final = Path.Combine(directory, "clip.mp4");
        try
        {
            var prepared = await service.PrepareAsync(new(info.Source.PageUrl, "00:00:01:00", "00:00:02:00", final), info, stop.Token);
            Assert.Equal(OperationStatus.Succeeded, prepared.Status); Assert.Equal(0, workspace.Calls); Assert.Equal(0, pipeline.Inspections);
            Assert.False(Directory.Exists(workRoot)); Assert.False(File.Exists(final)); Assert.NotEmpty(prepared.Data!.PendingChecks);
            Assert.Equal(fault == "fps-unknown", prepared.Data.Range is null);
            if (fault == "fps-changed") metadata.Info = new(info.Source, null, info.Duration, null, null, null, VideoState.Completed, [format with { FrameRate = F.Rate(60) }], []);
            var events = new List<JobProgress>();
            var progress = new Progress(p => { events.Add(p); if (p.Stage == JobStage.Finalizing) { if (fault == "after-finalize") stop.Cancel(); if (fault == "collision-final") File.WriteAllText(final, "user"); } });
            var result = await service.ExecuteAsync(prepared.Data, progress, stop.Token);
            if (fault is "success" or "after-finalize" or "fps-unknown" or "cleanup-lock") { Assert.Equal(OperationStatus.Succeeded, result.Status); Assert.Equal(final, result.OutputPath); }
            else if (fault is "before-finalize" or "handle" or "workspace-cancel") { Assert.Equal(OperationStatus.Cancelled, result.Status); Assert.Null(result.OutputPath); Assert.False(File.Exists(final)); }
            else { Assert.Equal(OperationStatus.Failed, result.Status); Assert.Null(result.OutputPath); }
            if (fault == "collision-final") Assert.Equal("user", await File.ReadAllTextAsync(final, TestContext.Current.CancellationToken));
            if (fault == "termination") { Assert.Equal(OperationErrorCode.ProcessTerminationFailed, result.Error!.Code); Assert.NotEmpty(result.RemainingTemporaryPaths); }
            if (fault is "handle" or "cleanup-lock") { Assert.NotEmpty(result.Warnings); Assert.NotEmpty(result.RemainingTemporaryPaths); }
            if (fault == "workspace-cancel") { Assert.Contains("partial cleanup", result.Warnings); Assert.Contains(workRoot, result.RemainingTemporaryPaths); Assert.Equal(0, pipeline.Inspections); }
            if (fault == "fps-changed") { Assert.Equal(0, workspace.Calls); Assert.Equal(0, pipeline.Inspections); }
            else if (fault != "workspace-cancel") Assert.Equal(1, pipeline.Processing);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(prepared.Data, null, TestContext.Current.CancellationToken));
        }
        finally
        {
            pipeline.Held?.Dispose();
            if (workspace.Session is not null) await workspace.Session.CleanupAsync(WorkspaceCleanupMode.DeleteOwnedTemporaryFiles);
            File.Delete(final); if (Directory.Exists(workRoot)) Directory.Delete(workRoot); Directory.Delete(directory);
        }
    }
}
