using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

// The job owner queries this even after a failed/cancelled operation (whose Data is null).
public interface ISourceResourceAccess
{
    SourceOwnedResources GetOwnedResources(Guid jobId);
}
