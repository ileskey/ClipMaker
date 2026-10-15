using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;

namespace ClipDownloader.Application.Contracts;

public interface IOutputPathValidator
{
    OperationResult<string> Validate(string outputPath, OperationErrorCategory category = OperationErrorCategory.Input);
}
public interface IOutputWorkspace
{
    Task<OperationResult<IOutputSession>> CreateAsync(Guid jobId, string finalPath, CancellationToken cancellationToken);
}
public interface IOutputSession
{
    Guid JobId { get; }
    Guid StagingFileId { get; }
    string WorkDirectory { get; }
    string StagingPath { get; }
    string FinalPath { get; }
    void TrackOwnedFiles(IEnumerable<MediaOwnedProcessingFile> files);
    Task<OperationResult<string>> CommitAsync(MediaOwnedProcessingFile verifiedStaging);
    Task<WorkspaceCleanupResult> CleanupAsync(WorkspaceCleanupMode mode);
}
