using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface ISelectedSourceInspectionService
{
    Task<OperationResult<SelectedSourceInspection>> InspectAsync(
        SelectedSourceInspectionRequest request, CancellationToken cancellationToken);
}
