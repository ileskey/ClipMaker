using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.YtDlp;

public sealed class YtDlpVideoInfoService : IVideoInfoService
{
    private readonly YtDlpMetadataQuery query;
    public YtDlpVideoInfoService(IToolExecutionDiagnostics diagnostics, IToolLocator locator,
        ToolPathSettings settings, IProcessRunner runner, string workingDirectory)
    {
        query = new(diagnostics, locator, settings, runner, workingDirectory);
    }

    public async Task<OperationResult<VideoInfo>> GetVideoInfoAsync(Uri url, CancellationToken cancellationToken)
    {
        var result = await query.GetMetadataAsync(url, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            OperationStatus.Succeeded => OperationResult<VideoInfo>.Succeeded(result.Data!.Info),
            OperationStatus.Cancelled => OperationResult<VideoInfo>.Cancelled(),
            _ => OperationResult<VideoInfo>.Failed(result.Error!)
        };
    }
}
