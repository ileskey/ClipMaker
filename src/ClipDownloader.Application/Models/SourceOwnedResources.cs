namespace ClipDownloader.Application.Models;

public sealed class SourceOwnedResources
{
    public Guid JobId { get; }
    public IReadOnlyList<OwnedMediaFile> Files { get; }
    public bool HasUnconfirmedProcess { get; }
    public bool NeedsCleanup { get; }
    public string? Warning => HasUnconfirmedProcess ? "source 프로세스의 종료를 확인하지 못해 자원을 보존했습니다." :
        NeedsCleanup && Files.Count != 0 ? "source 작업 실패 또는 취소 후 남은 임시 파일이 있습니다." : null;

    public SourceOwnedResources(Guid jobId, IEnumerable<OwnedMediaFile> files, bool hasUnconfirmedProcess, bool needsCleanup)
    {
        if(jobId == Guid.Empty) throw new ArgumentException("Job identity is required.",nameof(jobId));
        ArgumentNullException.ThrowIfNull(files);
        var copy=files.ToArray();
        if(copy.Any(f=>f is null) || copy.Select(f=>f.Id).Distinct().Count()!=copy.Length)
            throw new ArgumentException("Unique owned files are required.",nameof(files));
        JobId=jobId; Files=Array.AsReadOnly(copy); HasUnconfirmedProcess=hasUnconfirmedProcess; NeedsCleanup=needsCleanup;
    }
}
