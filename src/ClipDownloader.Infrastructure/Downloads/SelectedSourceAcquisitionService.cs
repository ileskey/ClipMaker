using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using System.Numerics;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class SourceInspectionContext(SelectedSourceInspection inspection, SelectedSourceLocators locators,
    VerifiedHlsNativeReference native, VerifiedHlsNativeReference? audio = null, long? budgetUsed = null)
{
    internal SelectedSourceInspection Inspection { get; } = inspection;
    internal SelectedSourceLocators Locators { get; } = locators;
    internal VerifiedHlsNativeReference Native { get; } = native;
    internal VerifiedHlsNativeReference? Audio { get; } = audio;
    internal long BudgetUsed { get; } = budgetUsed ?? native.Reference.Reference.BudgetUsed;
}

// Scoped source operations; hosts register the same instance for inspection/resource access.
public sealed partial class SelectedSourceAcquisitionService : ISelectedSourceInspectionService, IClipDownloadService, ISourceResourceAccess, IDisposable
{
    private sealed class JobState(SelectedSourceInspectionRequest request)
    {
        internal SelectedSourceInspectionRequest Request { get; } = request;
        internal Dictionary<Guid,OwnedMediaFile> Files { get; } = [];
        internal bool Unconfirmed { get; set; }
        internal bool NeedsCleanup { get; set; }
    }
    private readonly object stateLock = new();
    private readonly Dictionary<Guid,JobState> jobs = [];
    private readonly Dictionary<Guid,SourceInspectionContext> inspections = [];
    private readonly SemaphoreSlim operations = new(1,1);
    private readonly Func<SelectedSourceSnapshot,CancellationToken,Task<OperationResult<SelectedSourceLocators>>> resolve;
    private readonly Func<SelectedSourceInspectionRequest,SelectedSourceLocators,CancellationToken,Task<HlsInitialReferenceInspectionResult>> inspect;
    private readonly BoundedSourceHttpReader? http;
    private readonly InitialReferenceProbe? probe;
    private readonly PresentationRelationshipInspector? relationship;

    public SelectedSourceAcquisitionService(IToolExecutionDiagnostics diagnostics, IToolLocator locator,
        ToolPathSettings settings, IProcessRunner runner, string workingDirectory)
    {
        resolve=new YtDlpSourceLocatorService(new YtDlpMetadataQuery(diagnostics,locator,settings,runner,workingDirectory)).ResolveAsync;
        http=new(); probe=new(locator,settings,runner);
        relationship = new(http, new(locator, settings, runner));
        inspect=(request,locators,token)=>new HlsInitialReferenceInspector(http,(reference,probeToken)=>
        {
            Register(request.JobId,[reference.File]);
            return probe.ProbeAsync(reference,probeToken);
        }).InspectAsync(request,locators,token);
    }
    internal SelectedSourceAcquisitionService(
        Func<SelectedSourceSnapshot,CancellationToken,Task<OperationResult<SelectedSourceLocators>>> resolve,
        Func<SelectedSourceInspectionRequest,SelectedSourceLocators,CancellationToken,Task<HlsInitialReferenceInspectionResult>> inspect,
        BoundedSourceHttpReader? http=null, InitialReferenceProbe? probe=null, PresentationRelationshipInspector? relationship=null)
    { ArgumentNullException.ThrowIfNull(resolve); ArgumentNullException.ThrowIfNull(inspect); this.resolve=resolve; this.inspect=inspect; this.http=http;this.probe=probe; this.relationship=relationship; }

    public SourceOwnedResources GetOwnedResources(Guid jobId)
    {
        lock(stateLock) return jobs.TryGetValue(jobId,out var state) ? new(jobId,state.Files.Values,state.Unconfirmed,state.NeedsCleanup) : new(jobId,[],false,false);
    }
    private void Register(Guid job,IEnumerable<OwnedMediaFile> files)
    { lock(stateLock) foreach(var file in files) jobs[job].Files[file.Id]=file; }
    private void Failed(Guid job,OperationError? error)
    { lock(stateLock) if(jobs.TryGetValue(job,out var state)) {state.NeedsCleanup=true;state.Unconfirmed |= error?.Code==OperationErrorCode.ProcessTerminationFailed;} }

    public async Task<OperationResult<SelectedSourceInspection>> InspectAsync(SelectedSourceInspectionRequest request,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool entered=false;
        try
        {
            await operations.WaitAsync(cancellationToken).ConfigureAwait(false); entered=true;
            lock(stateLock)
            {
                if(jobs.TryGetValue(request.JobId,out var previous))
                {
                    if(previous.Unconfirmed) return Failure(OperationErrorCode.ProcessTerminationFailed,"PreviousTerminationUnconfirmed");
                    if(!previous.Request.Selection.Matches(request.Selection) || !StringComparer.OrdinalIgnoreCase.Equals(previous.Request.WorkDirectory,request.WorkDirectory))
                        return Failure(OperationErrorCode.VerificationFailed,"JobBinding");
                }
                else jobs.Add(request.JobId,new(request));
            }
            var locators=await resolve(request.Selection,cancellationToken).ConfigureAwait(false);
            if(locators.Status!=OperationStatus.Succeeded) {Failed(request.JobId,locators.Error);return Translate(locators.Status,locators.Error);}
            if (http is not null && probe is not null && locators.Data!.Video.Format.Transport == FormatTransport.Https &&
                locators.Data.Audio is { Format.Transport: FormatTransport.Https })
                return await InspectIndexedAsync(request, locators.Data, cancellationToken).ConfigureAwait(false);
            var initial=await inspect(request,locators.Data!,cancellationToken).ConfigureAwait(false);
            Register(request.JobId,initial.RemainingFiles);
            if(initial.Data is { } observed) Register(request.JobId,[observed.Reference.File]);
            if(initial.Status!=OperationStatus.Succeeded) {Failed(request.JobId,initial.Error);return Translate(initial.Status,initial.Error);}
            cancellationToken.ThrowIfCancellationRequested();
            if(initial.Data!.Reference is not HlsCollectedReference hls || hls.ActualPlaylistRange.Start!=SignedMediaTime.Zero)
            {Failed(request.JobId,null);return Failure(OperationErrorCode.VerificationFailed,"InitialReferenceRequired");}
            var verified=new HlsNativeReferenceVerifier().Verify(initial.Data);
            if(verified.Status!=OperationStatus.Succeeded) {Failed(request.JobId,verified.Error);return Translate(verified.Status,verified.Error);}
            var native=verified.Data!;
            var video=native.Streams.Single(s=>s.Stream.Kind==MediaStreamKind.Video);
            bool audio=native.Streams.Any(s=>s.Stream.Kind==MediaStreamKind.Audio);
            if(request.Selection.Selection.VideoFormat.AudioStream==StreamPresence.Present && !audio ||
                request.Selection.Selection.VideoFormat.AudioStream==StreamPresence.Absent && audio)
            {Failed(request.JobId,null);return Failure(OperationErrorCode.VerificationFailed,"AudioPresenceContradiction");}
            var rate=request.Selection.Selection.VideoFormat.FrameRate ?? locators.Data!.Video.Format.FrameRate;
            var observedRate=video.Stream.DeclaredRate ?? MeasuredRate(native,video);
            if(rate is not null && observedRate is { } reported && reported!=rate)
            {Failed(request.JobId,null);return Failure(OperationErrorCode.VerificationFailed,"SelectedFrameRateChanged");}
            if(observedRate is null) rate=null;
            if(rate is null && request.Purpose.HasFlag(SourceInspectionPurpose.FrameRate)) rate=observedRate;
            var id=Guid.NewGuid(); var stream=new MediaStreamReference(initial.Data.Reference.File.Id,video.Stream.Index,MediaStreamKind.Video);
            var scope=new MediaObservationScope(id,stream,MediaInspectionKind.Summary,MediaTimeBasis.Native,video.ObservedRange,ObservationCompleteness.Complete);
            var partial=new MediaObservationScope(id,stream,MediaInspectionKind.Summary,MediaTimeBasis.Native,video.ObservedRange,ObservationCompleteness.Partial);
            var fps=rate is null ? MediaInspectionFact<FrameRate>.Unknown(partial,MediaInspectionIssue.NotInspected) : MediaInspectionFact<FrameRate>.Verified(rate,scope);
            bool timelineRequested=request.Purpose.HasFlag(SourceInspectionPurpose.SourceTimeline);
            var earliest=native.Streams.MinBy(s=>s.ObservedRange.Start)!;
            var originScope=new MediaObservationScope(id,new(initial.Data.Reference.File.Id,earliest.Stream.Index,earliest.Stream.Kind),
                MediaInspectionKind.Summary,MediaTimeBasis.Native,earliest.ObservedRange,ObservationCompleteness.Complete);
            var origin=timelineRequested ? MediaInspectionFact<SignedMediaTime>.Verified(native.EarliestPresentation,originScope) : MediaInspectionFact<SignedMediaTime>.Unknown(partial,MediaInspectionIssue.NotInspected);
            var axis=timelineRequested ? MediaInspectionFact<SourceAxisRelationship>.Verified(new(request.Selection.Selection.VideoFormat.Id,null,SignedMediaTime.Zero),scope) :
                MediaInspectionFact<SourceAxisRelationship>.Unknown(partial,MediaInspectionIssue.NotInspected);
            var result=new SelectedSourceInspection(id,request,[initial.Data.Reference.File],fps,new(origin,axis,
                MediaInspectionFact<SignedMediaTime>.Unknown(partial,MediaInspectionIssue.NotInspected),audio ? StreamPresence.Present : StreamPresence.Absent));
            cancellationToken.ThrowIfCancellationRequested();
            lock(stateLock) inspections.Add(id,new(result,locators.Data!,native));
            return OperationResult<SelectedSourceInspection>.Succeeded(result);
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {Failed(request.JobId,null);return OperationResult<SelectedSourceInspection>.Cancelled();}
        finally {if(entered) operations.Release();}
    }
    private static FrameRate? MeasuredRate(VerifiedHlsNativeReference native,VerifiedNativeStream video)
    {
        var frames=native.Reference.Observations.Frames.Where(f=>f.StreamIndex==video.Stream.Index).OrderBy(f=>f.Pts).ToArray();
        if(frames.Length<2) return null;
        BigInteger delta=(BigInteger)frames[1].Pts!.Value-frames[0].Pts!.Value;
        if(delta<=0 || delta>long.MaxValue || frames.Skip(1).Select((f,i)=>(BigInteger)f.Pts!.Value-frames[i].Pts!.Value).Any(d=>d!=delta)) return null;
        return FrameRate.TryCreate((long)video.Stream.TimeBase.Denominator,(long)delta,out var rate,out _) ? rate : null;
    }
    private static OperationResult<SelectedSourceInspection> Translate(OperationStatus status,OperationError? error) => status==OperationStatus.Cancelled ?
        OperationResult<SelectedSourceInspection>.Cancelled() : OperationResult<SelectedSourceInspection>.Failed(error!);
    private static OperationResult<SelectedSourceInspection> Failure(OperationErrorCode code,string field)=>OperationResult<SelectedSourceInspection>.Failed(
        new(code,OperationErrorCategory.Execution,"선택 source의 초기 검사를 완료하지 못했습니다.",field));
    public void Dispose()=>http?.Dispose();
}
