using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

public sealed partial class SelectedSourceAcquisitionService
{
    private async Task<OperationResult<AcquiredMedia>> AcquireIndexedAsync(AcquisitionRequest request,
        SourceInspectionContext context, SignedMediaTime origin, CancellationToken token)
    {
        if (http is null || probe is null || relationship is null) return AcquireFailure(OperationErrorCode.UnsupportedFormat, "AcquisitionCompositionMissing");
        var budget = request.Inspection.Request.Budget;
        long used = context.BudgetUsed;
        if (used >= budget.MaximumBytes) return AcquireFailure(OperationErrorCode.UnsupportedFormat, "SourceByteBudget");
        var resolved = await resolve(request.Selection, token).ConfigureAwait(false);
        if (resolved.Status != OperationStatus.Succeeded) return AcquireTranslate(resolved.Status, resolved.Error);
        var refreshRequest = new SelectedSourceInspectionRequest(request.JobId, request.Selection, request.Inspection.Request.Purpose,
            request.WorkDirectory, new(budget.MaximumBytes - used, budget.MaximumPresentationSpan));
        var refresh = await InspectIndexedAsync(refreshRequest, resolved.Data!, token).ConfigureAwait(false);
        if (refresh.Status != OperationStatus.Succeeded) return AcquireTranslate(refresh.Status, refresh.Error);
        SourceInspectionContext fresh;
        lock (stateLock) fresh = inspections[refresh.Data!.InspectionId];
        if (fresh.Audio is null || refresh.Data!.Timeline.SharedAxis.State != MediaInspectionState.Verified ||
            fresh.Native.Reference.Reference.Sha256 != context.Native.Reference.Reference.Sha256 ||
            fresh.Audio.Reference.Reference.Sha256 != context.Audio!.Reference.Reference.Sha256 ||
            refresh.Data.Timeline.Origin.Value != origin || refresh.Data.FrameRate.Value != request.Inspection.FrameRate.Value)
            return AcquireFailure(OperationErrorCode.VerificationFailed, "SourceReferenceChanged");
        used += fresh.BudgetUsed;
        var begin = SignedMediaTime.FromTimecode(request.Range.Start) + origin;
        var end = SignedMediaTime.FromTimecode(request.Range.End) + origin;
        PresentationRange.TryCreate(begin, end, out var required, out _);
        var inspector = new IndexedSourceInspector(http, (reference, probeToken) =>
        {
            Register(request.JobId, [reference.File]);
            return probe.ProbeAsync(reference, probeToken);
        });
        var acquired = new List<VerifiedHlsNativeReference>();
        foreach (var pair in new[] { (Locator: resolved.Data!.Video, Initial: fresh.Native), (Locator: resolved.Data.Audio!, Initial: fresh.Audio) })
        {
            if (budget.MaximumBytes - used < IndexedSourceInspector.PrefixBytes) return AcquireFailure(OperationErrorCode.UnsupportedFormat, "SourceByteBudget");
            var source = await inspector.ReadIndexAsync(pair.Locator, new(budget.MaximumBytes - used, budget.MaximumPresentationSpan), token).ConfigureAwait(false);
            if (source.Status != OperationStatus.Succeeded) return AcquireTranslate(source.Status, source.Error);
            used += source.Data!.PrefixBytes;
            var references = source.Data.Index.References;
            var initial = (IndexedCollectedReference)pair.Initial.Reference.Reference;
            var margin = initial.ActualIndexRange.End - initial.ActualIndexRange.Start;
            // Accurate preparation may only use a bounded prefix from the confirmed source beginning.
            // Budget failure is explicit; this never switches to a whole-resource download.
            var start = request.Selection.Mode == ClipMode.Accurate ? references[0].IndexRange.Start : begin - margin;
            if (start.CompareTo(references[0].IndexRange.Start) < 0) start = references[0].IndexRange.Start;
            var finish = request.Selection.Mode == ClipMode.Accurate || pair.Initial.Streams.Single().Stream.Kind == MediaStreamKind.Audio ? end : end + margin;
            if (finish.CompareTo(references[^1].IndexRange.End) > 0) finish = references[^1].IndexRange.End;
            if (!PresentationRange.TryCreate(start, finish, out var window, out _)) return AcquireFailure(OperationErrorCode.VerificationFailed, "SourceWindowUnavailable");
            var result = await inspector.InspectAsync(source.Data, window!, request.WorkDirectory, budget, used, token).ConfigureAwait(false);
            Register(request.JobId, result.RemainingFiles);
            if (result.Status != OperationStatus.Succeeded) return AcquireTranslate(result.Status, result.Error);
            // The unchanged movie header provides the global affine edit transform; independently
            // compare actual target native support with the direct SIDX coordinate declarations.
            var verified = new IndexedNativeAcquisitionVerifier().Verify(pair.Initial, result.Data!, required!);
            if (verified.Status != OperationStatus.Succeeded) return AcquireTranslate(verified.Status, verified.Error);
            var current = verified.Data!;
            var file = (IndexedCollectedReference)result.Data!.Reference;
            used = file.BudgetUsed;
            acquired.Add(current);
        }
        var id = Guid.NewGuid();
        var mappings = acquired.Select(native =>
        {
            var stream = native.Streams.Single().Stream;
            var scope = new MediaObservationScope(id, new(native.Reference.Reference.File.Id, stream.Index, stream.Kind),
                stream.Kind == MediaStreamKind.Video ? MediaInspectionKind.Frames : MediaInspectionKind.Samples,
                MediaTimeBasis.Native, required!, ObservationCompleteness.Complete);
            var relationScope = new MediaObservationScope(id, scope.Stream, scope.Kind, MediaTimeBasis.Native,
                native.Streams.Single().ObservedRange, ObservationCompleteness.Complete);
            return new SourceStreamMapping(MediaInspectionFact<SignedMediaTime>.Verified(SignedMediaTime.Zero, relationScope),
                MediaInspectionFact<PresentationRange>.Verified(required!, scope));
        }).ToArray();
        token.ThrowIfCancellationRequested();
        return OperationResult<AcquiredMedia>.Succeeded(new(id, request, acquired.Select(n => n.Reference.Reference.File), mappings));
    }
}
