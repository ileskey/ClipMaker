using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed partial class SelectedSourceAcquisitionService
{
    public async Task<OperationResult<AcquiredMedia>> AcquireAsync(AcquisitionRequest request,
        IProgress<AcquisitionProgress>? progress,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);bool entered=false,completed=false;
        try
        {
            await operations.WaitAsync(cancellationToken).ConfigureAwait(false);entered=true;
            SourceInspectionContext context;
            lock(stateLock)
            {
                if(!inspections.TryGetValue(request.Inspection.InspectionId,out var found) || !ReferenceEquals(found.Inspection,request.Inspection))
                    return AcquireFailure(OperationErrorCode.VerificationFailed,"InspectionBinding");
                context=found;
                if(jobs[request.JobId].Unconfirmed) return AcquireFailure(OperationErrorCode.ProcessTerminationFailed,"PreviousTerminationUnconfirmed");
            }
            if(request.Inspection.Timeline.Origin.Value is not { } origin || request.Inspection.Timeline.SharedAxis.State!=MediaInspectionState.Verified)
                return AcquireFailure(OperationErrorCode.VerificationFailed,"SourceTimelineUnverified");
            if (context.Audio is not null) return AcquireFailure(OperationErrorCode.UnsupportedFormat, "IndexedTargetStrategyUnavailable");
            if(http is null || probe is null) return AcquireFailure(OperationErrorCode.UnsupportedFormat,"AcquisitionCompositionMissing");
            var locators=await resolve(request.Selection,cancellationToken).ConfigureAwait(false);
            if(locators.Status!=OperationStatus.Succeeded) {Failed(request.JobId,locators.Error);return AcquireTranslate(locators.Status,locators.Error);}
            var budget=request.Inspection.Request.Budget;
            long already=context.BudgetUsed;
            if(already>=budget.MaximumBytes) return AcquireFailure(OperationErrorCode.UnsupportedFormat,"SourceByteBudget");
            var refreshRequest=new SelectedSourceInspectionRequest(request.JobId,request.Selection,request.Inspection.Request.Purpose,
                request.WorkDirectory,new(budget.MaximumBytes-already,budget.MaximumPresentationSpan));
            var refresh=await inspect(refreshRequest,locators.Data!,cancellationToken).ConfigureAwait(false);
            Register(request.JobId,refresh.RemainingFiles);
            if(refresh.Data is { } refreshed) Register(request.JobId,[refreshed.Reference.File]);
            if(refresh.Status!=OperationStatus.Succeeded) {Failed(request.JobId,refresh.Error);return AcquireTranslate(refresh.Status,refresh.Error);}
            cancellationToken.ThrowIfCancellationRequested();
            if(refresh.Data!.Reference.Sha256!=context.Native.Reference.Reference.Sha256 || refresh.Playlist is null)
            {Failed(request.JobId,null);return AcquireFailure(OperationErrorCode.VerificationFailed,"SourceReferenceChanged");}
            already=checked(already+refresh.Data.Reference.BudgetUsed);
            var playlist=refresh.Playlist;
            var video=context.Native.Streams.Single(s=>s.Stream.Kind==MediaStreamKind.Video);
            var sourceStart=SignedMediaTime.FromTimecode(request.Range.Start)+origin;
            var sourceEnd=SignedMediaTime.FromTimecode(request.Range.End)+origin;
            var firstReference=(HlsCollectedReference)context.Native.Reference.Reference;
            var firstSpan=firstReference.ActualPlaylistRange.End-firstReference.ActualPlaylistRange.Start;
            var fetchStart=sourceStart-video.ObservedRange.Start-firstSpan;
            if(fetchStart.CompareTo(SignedMediaTime.Zero)<0) fetchStart=SignedMediaTime.Zero;
            var fetchEnd=sourceEnd-video.ObservedRange.Start+firstSpan;
            if(fetchEnd.CompareTo(playlist.Duration)>0) fetchEnd=playlist.Duration;
            if(!PresentationRange.TryCreate(fetchStart,fetchEnd,out var window,out _)) return AcquireFailure(OperationErrorCode.VerificationFailed,"SourceWindowUnavailable");
            var plan=new HlsReferencePlanner().Plan(playlist,window!,budget.MaximumPresentationSpan,64);
            if(plan.Status!=OperationStatus.Succeeded) return AcquireTranslate(plan.Status,plan.Error);
            progress?.Report(new(request.JobId,null,"선택 원본 참조를 수집하고 있습니다."));
            var collected=await new HlsReferenceCollector(http).CollectAsync(Guid.NewGuid(),playlist,plan.Data!,locators.Data!.Video,
                request.WorkDirectory,budget,already,cancellationToken).ConfigureAwait(false);
            Register(request.JobId,collected.RemainingFiles);
            if(collected.Status!=OperationStatus.Succeeded) {Failed(request.JobId,collected.Error);return AcquireTranslate(collected.Status,collected.Error);}
            Register(request.JobId,[collected.Data!.File]);
            var observed=await probe.ProbeAsync(collected.Data,cancellationToken).ConfigureAwait(false);
            if(observed.Status!=OperationStatus.Succeeded) {Failed(request.JobId,observed.Error);return AcquireTranslate(observed.Status,observed.Error);}
            var verified=new HlsNativeReferenceVerifier().Verify(observed.Data!);
            if(verified.Status!=OperationStatus.Succeeded) {Failed(request.JobId,verified.Error);return AcquireTranslate(verified.Status,verified.Error);}
            var acquired=verified.Data!;
            foreach(var original in context.Native.Streams)
            {
                var actual=acquired.Streams.SingleOrDefault(s=>s.Stream.TrackId==original.Stream.TrackId);
                if(actual is null || actual.Configuration!=original.Configuration)
                {Failed(request.JobId,null);return AcquireFailure(OperationErrorCode.VerificationFailed,"SourceConfigurationChanged");}
            }
            var actualVideo=acquired.Streams.Single(s=>s.Stream.Kind==MediaStreamKind.Video);
            if(actualVideo.ObservedRange.Start!=video.ObservedRange.Start+collected.Data.ActualPlaylistRange.Start ||
                actualVideo.ObservedRange.End!=video.ObservedRange.Start+collected.Data.ActualPlaylistRange.End)
            {Failed(request.JobId,null);return AcquireFailure(OperationErrorCode.VerificationFailed,"PlaylistNativeClockChanged");}
            PresentationRange.TryCreate(sourceStart,sourceEnd,out var required,out _);
            var id=Guid.NewGuid();var mappings=new List<SourceStreamMapping>();
            foreach(var stream in acquired.Streams)
            {
                var beginning=context.Native.Streams.Single(s=>s.Stream.TrackId==stream.Stream.TrackId);
                bool knownLeadingEmpty=collected.Data.ActualPlaylistRange.Start==SignedMediaTime.Zero && sourceStart.CompareTo(origin)>=0 && sourceStart.CompareTo(beginning.ObservedRange.Start)<0;
                if((stream.ObservedRange.Start.CompareTo(sourceStart)>0 && !knownLeadingEmpty) || stream.ObservedRange.End.CompareTo(sourceEnd)<0)
                {Failed(request.JobId,null);return AcquireFailure(OperationErrorCode.VerificationFailed,"SourceCoverageIncomplete");}
                var reference=new MediaStreamReference(collected.Data.File.Id,stream.Stream.Index,stream.Stream.Kind);
                var scope=new MediaObservationScope(id,reference,stream.Stream.Kind==MediaStreamKind.Video?MediaInspectionKind.Frames:MediaInspectionKind.Samples,
                    MediaTimeBasis.Native,required,ObservationCompleteness.Complete);
                mappings.Add(new(MediaInspectionFact<SignedMediaTime>.Verified(SignedMediaTime.Zero,scope),
                    MediaInspectionFact<PresentationRange>.Verified(required!,scope)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(request.JobId,100,"원본 참조의 위치와 포함 범위를 확인했습니다."));
            var result=OperationResult<AcquiredMedia>.Succeeded(new(id,request,[collected.Data.File],mappings));
            completed=true;return result;
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {Failed(request.JobId,null);return OperationResult<AcquiredMedia>.Cancelled();}
        finally{if(entered){if(!completed)Failed(request.JobId,null);operations.Release();}}
    }
    private static OperationResult<AcquiredMedia> AcquireTranslate(OperationStatus status,OperationError? error)=>status==OperationStatus.Cancelled?
        OperationResult<AcquiredMedia>.Cancelled():OperationResult<AcquiredMedia>.Failed(error!);
    private static OperationResult<AcquiredMedia> AcquireFailure(OperationErrorCode code,string field)=>OperationResult<AcquiredMedia>.Failed(
        new(code,OperationErrorCategory.Execution,"선택 원본 구간을 확인된 관계로 획득하지 못했습니다.",field));
}
