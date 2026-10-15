using System.Security.Cryptography;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;

namespace ClipDownloader.Infrastructure.FileSystem;

internal sealed class OutputSession(Guid jobId, Guid stagingFileId, string workDirectory, string stagingPath, string finalPath) : IOutputSession
{
    public Guid JobId { get; } = jobId;
    public Guid StagingFileId { get; } = stagingFileId;
    public string WorkDirectory { get; } = workDirectory;
    public string StagingPath { get; } = stagingPath;
    public string FinalPath { get; } = finalPath;
    private readonly object gate = new();
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly Dictionary<string, MediaOwnedProcessingFile> owned = new(StringComparer.OrdinalIgnoreCase);
    private bool attempted, committed;

    public void TrackOwnedFiles(IEnumerable<MediaOwnedProcessingFile> files)
    {
        ArgumentNullException.ThrowIfNull(files); var copy = files.ToArray();
        foreach (var file in copy)
            if (file is null || file.Id == Guid.Empty || !Path.IsPathFullyQualified(file.Path) ||
                !(string.Equals(Path.GetDirectoryName(Path.GetFullPath(file.Path)), WorkDirectory, StringComparison.OrdinalIgnoreCase) ||
                    file.Id == StagingFileId && string.Equals(file.Path, StagingPath, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Only explicit owned job files or the session staging may be tracked.");
        lock (gate)
            foreach (var file in copy)
            {
                if (owned.TryGetValue(file.Path, out var existing) && existing.Id != file.Id) throw new ArgumentException("Owned identity changed.");
                if (!committed || !string.Equals(file.Path, StagingPath, StringComparison.OrdinalIgnoreCase)) owned[file.Path] = file;
            }
    }

    public async Task<OperationResult<string>> CommitAsync(MediaOwnedProcessingFile verifiedStaging)
    {
        ArgumentNullException.ThrowIfNull(verifiedStaging);
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (attempted) throw new InvalidOperationException("Final commit may only be attempted once.");
                attempted = true;
                if (verifiedStaging.Id != StagingFileId || verifiedStaging.Path != StagingPath || verifiedStaging.Content is null ||
                    !owned.TryGetValue(StagingPath, out var tracked) || tracked.Id != verifiedStaging.Id || tracked.Content != verifiedStaging.Content)
                    return Fail(OperationErrorCode.VerificationFailed, "StagingIdentity");
            }
            if (!await Matches(StagingPath, verifiedStaging.Content!).ConfigureAwait(false)) return Fail(OperationErrorCode.VerificationFailed, "StagingChanged");
            File.Move(StagingPath, FinalPath, overwrite: false);
            lock (gate) { committed = true; owned.Remove(StagingPath); }
            // Detect a change across the path-based move. Never delete a final
            // file on verification failure or a subsequent cancellation.
            if (!await Matches(FinalPath, verifiedStaging.Content!).ConfigureAwait(false)) return Fail(OperationErrorCode.VerificationFailed, "FinalBytesChanged");
            return OperationResult<string>.Succeeded(FinalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return committed ? Fail(OperationErrorCode.VerificationFailed, "FinalBytesChanged") :
            Fail(File.Exists(FinalPath) || Directory.Exists(FinalPath) ? OperationErrorCode.OutputConflict : OperationErrorCode.OutputFailed, "OutputCommit"); }
        finally { operations.Release(); }
    }

    public async Task<WorkspaceCleanupResult> CleanupAsync(WorkspaceCleanupMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            MediaOwnedProcessingFile[] files; lock (gate) files = owned.Values.ToArray();
            var removed = new List<string>(); var remaining = new List<string>(); var warnings = new List<string>();
            foreach (var file in files)
            {
                if (mode == WorkspaceCleanupMode.PreservePotentiallyInUse) { if (File.Exists(file.Path)) remaining.Add(file.Path); continue; }
                try { File.Delete(file.Path); removed.Add(file.Path); lock (gate) owned.Remove(file.Path); }
                catch (DirectoryNotFoundException) { lock (gate) owned.Remove(file.Path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { remaining.Add(file.Path); }
            }
            if (mode == WorkspaceCleanupMode.PreservePotentiallyInUse)
            {
                if (Directory.Exists(WorkDirectory)) remaining.Add(WorkDirectory);
            }
            else try { Directory.Delete(WorkDirectory, recursive: false); removed.Add(WorkDirectory); }
                catch (DirectoryNotFoundException) { }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { remaining.Add(WorkDirectory); }
            if (remaining.Count != 0) warnings.Add(mode == WorkspaceCleanupMode.PreservePotentiallyInUse ?
                "파일 또는 프로세스 수명을 확인하지 못해 사용 중일 수 있는 작업 파일을 보존했습니다." : "일부 소유 임시 파일 또는 작업 폴더를 정리하지 못했습니다.");
            return new(removed, remaining, warnings);
        }
        finally { operations.Release(); }
    }
    private static async Task<bool> Matches(string path, MediaFileContentIdentity expected)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (input.Length != expected.Bytes) return false;
        return Convert.ToHexString(await SHA256.HashDataAsync(input).ConfigureAwait(false)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase) && input.Length == expected.Bytes;
    }
    private static OperationResult<string> Fail(OperationErrorCode code, string field) => OperationResult<string>.Failed(
        new(code, OperationErrorCategory.Execution, "검증한 staging의 최종 파일 확정에 실패했습니다.", field));
}
