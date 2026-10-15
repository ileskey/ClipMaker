namespace ClipDownloader.Application.Models;

public enum WorkspaceCleanupMode { DeleteOwnedTemporaryFiles, PreservePotentiallyInUse }
public sealed class WorkspaceCleanupResult
{
    public IReadOnlyList<string> RemovedPaths { get; }
    public IReadOnlyList<string> RemainingPaths { get; }
    public IReadOnlyList<string> Warnings { get; }
    public WorkspaceCleanupResult(IEnumerable<string> removedPaths, IEnumerable<string> remainingPaths, IEnumerable<string> warnings)
    {
        RemovedPaths = Array.AsReadOnly(removedPaths.ToArray()); RemainingPaths = Array.AsReadOnly(remainingPaths.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
}
