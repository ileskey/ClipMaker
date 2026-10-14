using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.FileSystem;

public sealed class OutputWorkspace(IOutputPathValidator validator, string? workRoot = null) : IOutputWorkspace
{
    private readonly string root = Path.GetFullPath(workRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipMaker", "ClipDownloader", "jobs"));
    public Task<OperationResult<IOutputSession>> CreateAsync(Guid jobId, string finalPath, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job identity is required.");
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(OperationResult<IOutputSession>.Cancelled());
        var validation = validator.Validate(finalPath, OperationErrorCategory.Execution);
        if (validation.Status != OperationStatus.Succeeded) return Task.FromResult(OperationResult<IOutputSession>.Failed(validation.Error!));
        if (!Path.IsPathFullyQualified(finalPath)) return Task.FromResult(OperationResult<IOutputSession>.Failed(
            new(OperationErrorCode.InvalidOutputPath, OperationErrorCategory.Execution, "실행에는 준비된 절대 출력 경로가 필요합니다.", "Output")));
        string directory = Path.Combine(root, jobId.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
        bool created = false;
        try
        {
            Directory.CreateDirectory(root);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException();
            Directory.CreateDirectory(directory); created = true;
            if (cancellationToken.IsCancellationRequested) { Directory.Delete(directory); return Task.FromResult(OperationResult<IOutputSession>.Cancelled()); }
            Guid stagingId = Guid.NewGuid();
            string staging = Path.Combine(Path.GetDirectoryName(validation.Data!)!, ".clipmaker-" + stagingId.ToString("N") + ".tmp.mp4");
            return Task.FromResult(OperationResult<IOutputSession>.Succeeded(new OutputSession(jobId, stagingId, directory, staging, validation.Data!)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            string[] remaining = [];
            if (created) try { Directory.Delete(directory); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { remaining = [directory]; }
            return Task.FromResult(OperationResult<IOutputSession>.Failed(new(OperationErrorCode.OutputFailed, OperationErrorCategory.Execution,
                "출력 작업 공간을 생성하지 못했습니다.", "Workspace"), remaining.Length == 0 ? [] : ["부분 생성한 작업 폴더를 정리하지 못했습니다."], remaining));
        }
    }
}
