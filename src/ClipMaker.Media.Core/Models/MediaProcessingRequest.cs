namespace ClipMaker.Media.Core.Models;

public sealed class MediaProcessingRequest
{
    public Guid OperationId { get; }
    public Guid OutputFileId { get; }
    public MediaProcessingPlan Plan { get; }
    public string StagingPath { get; }
    public MediaAudioRenderInput? AudioRender { get; }
    public MediaProcessingRequest(Guid operationId, Guid outputFileId, MediaProcessingPlan plan, string stagingPath, MediaAudioRenderInput? audioRender = null)
    {
        if (operationId == Guid.Empty || outputFileId == Guid.Empty) throw new ArgumentException("Operation/output identities are required.");
        ArgumentNullException.ThrowIfNull(plan); ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);
        if (!Path.IsPathFullyQualified(stagingPath)) throw new ArgumentException("An absolute staging path is required.", nameof(stagingPath));
        OperationId = operationId; OutputFileId = outputFileId; Plan = plan; StagingPath = Path.GetFullPath(stagingPath);
        AudioRender = audioRender;
    }
}

public sealed record MediaOwnedProcessingFile(Guid Id, string Path, MediaFileContentIdentity? Content);
public sealed class MediaProcessingResources
{
    public IReadOnlyList<MediaOwnedProcessingFile> Files { get; }
    public bool HasUnconfirmedProcess { get; }
    public bool NeedsCleanup { get; }
    public MediaProcessingResources(IEnumerable<MediaOwnedProcessingFile> files, bool hasUnconfirmedProcess, bool needsCleanup)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = Array.AsReadOnly(files.ToArray()); HasUnconfirmedProcess = hasUnconfirmedProcess; NeedsCleanup = needsCleanup;
    }
}

// A captured staging output, not final verification or a committed user clip.
public sealed record MediaProcessingResult(MediaProcessingRequest Request, MediaOwnedProcessingFile Staging, string FfmpegVersion);
public sealed record MediaProcessingProgress(double? Percent, string Message);
