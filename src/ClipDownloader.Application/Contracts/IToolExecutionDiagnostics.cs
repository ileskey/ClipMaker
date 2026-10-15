using ClipDownloader.Application.Models;

namespace ClipDownloader.Application.Contracts;

public interface IToolExecutionDiagnostics
{
    Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(
        ToolDiagnosticRequest request, CancellationToken cancellationToken);
}
