using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed partial class SelectedSourceAcquisitionService
{
    private async Task<OperationResult<SelectedSourceInspection>> InspectIndexedAsync(SelectedSourceInspectionRequest request,
        SelectedSourceLocators locators, CancellationToken token)
    {
        bool completed = false;
        try
        {
            var inspector = new IndexedSourceInspector(http!, (reference, probeToken) =>
            {
                Register(request.JobId, [reference.File]);
                return probe!.ProbeAsync(reference, probeToken);
            });
            long used = 0;
            var native = new List<VerifiedHlsNativeReference>();
            foreach (var locator in new[] { locators.Video, locators.Audio! })
            {
                var result = await inspector.InspectInitialAsync(locator, request.WorkDirectory, request.Budget, used, token).ConfigureAwait(false);
                Register(request.JobId, result.RemainingFiles);
                if (result.Status != OperationStatus.Succeeded)
                { Failed(request.JobId, result.Error); return Translate(result.Status, result.Error); }
                var verified = new HlsNativeReferenceVerifier().Verify(result.Data!);
                if (verified.Status != OperationStatus.Succeeded) return Translate(verified.Status, verified.Error);
                if (verified.Data!.Streams.Single().Stream.Kind != (native.Count == 0 ? MediaStreamKind.Video : MediaStreamKind.Audio))
                    return Failure(OperationErrorCode.VerificationFailed, "SelectedStreamKindChanged");
                used = result.Data!.Reference.BudgetUsed;
                native.Add(verified.Data);
            }
            var video = native[0].Streams.Single();
            var rate = request.Selection.Selection.VideoFormat.FrameRate ?? locators.Video.Format.FrameRate;
            var measured = video.Stream.DeclaredRate ?? MeasuredRate(native[0], video);
            if (rate is not null && measured is not null && rate != measured)
                return Failure(OperationErrorCode.VerificationFailed, "SelectedFrameRateChanged");
            if (measured is null) rate = null;
            if (rate is null && request.Purpose.HasFlag(SourceInspectionPurpose.FrameRate)) rate = measured;
            var id = Guid.NewGuid();
            var stream = new MediaStreamReference(native[0].Reference.Reference.File.Id, video.Stream.Index, MediaStreamKind.Video);
            var complete = new MediaObservationScope(id, stream, MediaInspectionKind.Summary, MediaTimeBasis.Native,
                video.ObservedRange, ObservationCompleteness.Complete);
            var partial = new MediaObservationScope(id, stream, MediaInspectionKind.Summary, MediaTimeBasis.Native,
                video.ObservedRange, ObservationCompleteness.Partial);
            var fps = rate is null ? MediaInspectionFact<FrameRate>.Unknown(partial, MediaInspectionIssue.NotInspected) :
                MediaInspectionFact<FrameRate>.Verified(rate, complete);
            bool verifiedAxis = false;
            if (request.Purpose.HasFlag(SourceInspectionPurpose.SourceTimeline) && relationship is not null && locators.PresentationReferences.Count == 2)
            {
                var proof = await relationship.InspectAsync(request, locators, native[0], native[1], used,
                    file => Register(request.JobId, [file]), token).ConfigureAwait(false);
                if (proof.Status != OperationStatus.Succeeded)
                { Failed(request.JobId, proof.Error); return Translate(proof.Status, proof.Error); }
                used = proof.Data!.BudgetUsed;
                verifiedAxis = true;
            }
            var earliest = native.MinBy(n => n.EarliestPresentation)!;
            var first = earliest.Streams.Single();
            var originScope = new MediaObservationScope(id, new(earliest.Reference.Reference.File.Id, first.Stream.Index, first.Stream.Kind),
                MediaInspectionKind.Summary, MediaTimeBasis.Native, first.ObservedRange, ObservationCompleteness.Complete);
            var origin = verifiedAxis ? MediaInspectionFact<SignedMediaTime>.Verified(earliest.EarliestPresentation, originScope) :
                MediaInspectionFact<SignedMediaTime>.Unknown(partial, MediaInspectionIssue.NotInspected);
            var axis = verifiedAxis ? MediaInspectionFact<SourceAxisRelationship>.Verified(new(locators.Video.Format.Id, locators.Audio!.Format.Id,
                SignedMediaTime.Zero), complete) : MediaInspectionFact<SourceAxisRelationship>.Unknown(partial, MediaInspectionIssue.NotInspected);
            var resultInspection = new SelectedSourceInspection(id, request,
                native.Select(n => n.Reference.Reference.File), fps,
                new(origin, axis,
                    MediaInspectionFact<SignedMediaTime>.Unknown(partial, MediaInspectionIssue.NotInspected), StreamPresence.Present));
            token.ThrowIfCancellationRequested();
            lock (stateLock) inspections.Add(id, new(resultInspection, locators, native[0], native[1], used));
            completed = true;
            return OperationResult<SelectedSourceInspection>.Succeeded(resultInspection);
        }
        finally { if (!completed) Failed(request.JobId, null); }
    }
}
