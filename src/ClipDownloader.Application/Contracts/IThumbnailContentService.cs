using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface IThumbnailContentService
{
    Task<ThumbnailContentResult> GetAsync(Uri uri, CancellationToken cancellationToken);
}
