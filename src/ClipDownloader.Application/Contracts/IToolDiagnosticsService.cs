using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface IToolDiagnosticsService
{
    Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken cancellationToken);
}
