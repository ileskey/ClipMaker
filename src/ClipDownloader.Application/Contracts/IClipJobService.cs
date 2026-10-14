using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;

namespace ClipDownloader.Application.Contracts;

public interface IClipJobService
{
    Task<OperationResult<PreparedClip>> PrepareAsync(ClipRequest request, VideoInfo videoInfo, CancellationToken cancellationToken);
    Task<ClipJobResult> ExecuteAsync(PreparedClip preparedClip, IProgress<JobProgress>? progress, CancellationToken cancellationToken);
}
public interface IClipMediaService
{
    Task<OperationResult<VerifiedClipOutput>> ProcessAsync(AcquiredMedia acquired, Guid stagingFileId, string stagingPath,
        IProgress<JobProgress>? progress, CancellationToken cancellationToken);
    ClipMediaResources GetOwnedResources(Guid jobId);
}
public sealed class ClipMediaResources(IEnumerable<MediaOwnedProcessingFile> files, bool hasUnconfirmedProcess, bool hasUnconfirmedFileHandles = false)
{
    public IReadOnlyList<MediaOwnedProcessingFile> Files { get; } = Array.AsReadOnly(files.ToArray());
    public bool HasUnconfirmedProcess { get; } = hasUnconfirmedProcess;
    public bool HasUnconfirmedFileHandles { get; } = hasUnconfirmedFileHandles;
}
