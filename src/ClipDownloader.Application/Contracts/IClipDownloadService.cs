using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface IClipDownloadService
{
    Task<OperationResult<AcquiredMedia>> AcquireAsync(AcquisitionRequest request,
        IProgress<AcquisitionProgress>? progress, CancellationToken cancellationToken);
}
