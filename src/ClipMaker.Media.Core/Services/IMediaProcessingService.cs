using ClipMaker.Media.Core.Models;

namespace ClipMaker.Media.Core.Services;

public interface IMediaProcessingService
{
    Task<MediaOperationResult<MediaProcessingResult>> ProcessAsync(MediaProcessingRequest request,
        IProgress<MediaProcessingProgress>? progress = null, CancellationToken cancellationToken = default);
}
