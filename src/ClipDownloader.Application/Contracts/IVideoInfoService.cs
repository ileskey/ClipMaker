using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface IVideoInfoService
{
    Task<OperationResult<VideoInfo>> GetVideoInfoAsync(Uri url, CancellationToken cancellationToken);
}
