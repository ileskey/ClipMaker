using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

public sealed record ClipRequest(Uri Url, string Start, string End, string OutputPath, QualityOption? Quality = null, ClipMode Mode = ClipMode.Fast);
public enum JobStage { Preparing, Rechecking, SourceInspection, Downloading, InputInspection, Processing, OutputInspection, Finalizing, Cancelling, Cleaning, Completed }
public sealed record JobProgress(Guid JobId, JobStage Stage, double? Percent, string Message);
public sealed class PreparedClip
{
    public Guid JobId { get; }
    public SelectedSourceSnapshot Selection { get; }
    public TimecodeInput Start { get; }
    public TimecodeInput End { get; }
    public MediaTimeRange? Range { get; }
    public string FinalPath { get; }
    public string? Title { get; }
    public string? Uploader { get; }
    public SourceInspectionBudget Budget { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> PendingChecks { get; }
    internal PreparedClip(Guid jobId, SelectedSourceSnapshot selection, TimecodeInput start, TimecodeInput end,
        MediaTimeRange? range, string finalPath, IEnumerable<string> warnings, IEnumerable<string> pendingChecks, SourceInspectionBudget budget, string? title = null, string? uploader = null)
    {
        JobId = jobId; Selection = selection; Start = start; End = end; Range = range; FinalPath = finalPath;
        Warnings = Array.AsReadOnly(warnings.ToArray()); PendingChecks = Array.AsReadOnly(pendingChecks.ToArray());
        Title = title; Uploader = uploader;
        Budget = budget;
    }
}
public sealed class ClipJobResult
{
    public Guid JobId { get; }
    public OperationStatus Status { get; }
    public string? OutputPath { get; }
    public OperationError? Error { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> RemainingTemporaryPaths { get; }
    public ClipPresentationSummary? Presentation { get; }
    public ClipJobResult(Guid jobId, OperationStatus status, string? outputPath, OperationError? error,
        IEnumerable<string> warnings, IEnumerable<string> remainingTemporaryPaths, ClipPresentationSummary? presentation = null)
    {
        if (jobId == Guid.Empty || !Enum.IsDefined(status) || (status == OperationStatus.Succeeded) != (outputPath is not null) ||
            (status == OperationStatus.Failed) != (error is not null) || status != OperationStatus.Succeeded && presentation is not null)
            throw new ArgumentException("Only a committed success may expose output and verification.");
        JobId = jobId; Status = status; OutputPath = outputPath; Error = error;
        Warnings = Array.AsReadOnly(warnings.ToArray()); RemainingTemporaryPaths = Array.AsReadOnly(remainingTemporaryPaths.ToArray()); Presentation = presentation;
    }
}

// User coordinates, independent of temporary file paths that cleanup removes.
public sealed record ClipPresentationSummary(PresentationRange Requested, PresentationRange Video, PresentationRange? Audio,
    bool SourceEndVerified, long InsertedAudioZeroSamples, long DiscardedAudioOverlapSamples, long AudioPrimingSkippedSamples, long AudioDecodedTailPaddingSamples)
{
    internal static ClipPresentationSummary FromVerified(VerifiedClipOutput output)
    {
        var timeline = output.Plan.Inspection.Acquisition.Request.Inspection.Timeline;
        var origin = timeline.Origin.Value!;
        PresentationRange User(PresentationRange source)
        { PresentationRange.TryCreate(source.Start - origin, source.End - origin, out var user, out _); return user!; }
        var audio = output.Output.Streams.SingleOrDefault(s => s.Stream.Kind == ClipMaker.Media.Core.Models.MediaStreamKind.Audio);
        var counts = output.Processing.Request.AudioRender?.Counts;
        return new(User(output.Plan.SourceTarget), User(output.VideoSourcePresentation), output.AudioSourcePresentation is null ? null : User(output.AudioSourcePresentation),
            timeline.NativeEnd.State == ClipMaker.Media.Core.Models.MediaInspectionState.Verified, counts?.InsertedZeroSamples ?? 0, counts?.DiscardedOverlapSamples ?? 0,
            audio?.PrimingSkippedSamples ?? 0, audio?.DecodedTailPaddingSamples ?? 0);
    }
}
