using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Diagnostics;

public sealed class ToolDiagnosticsService(IToolLocator locator, ToolPathSettings settings)
    : IToolDiagnosticsService
{
    public Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ToolDiagnostic>>(() =>
        {
            var results = new List<ToolDiagnostic>();
            foreach (var name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = locator.Locate(new(name, settings.GetPath(name)), cancellationToken);
                results.Add(new(name, MapStatus(result.Status), result.IsUserSpecified,
                    result.SelectedPath, result.Source is { } source ? MapSource(source) : null,
                    Array.AsReadOnly(result.Inspections.Select(item => new ToolDiagnosticInspection(
                        item.Path, MapSource(item.Source), MapStatus(item.Status), item.FailureReason)).ToArray()),
                    result.FailureReason));
            }
            return results.AsReadOnly();
        }, cancellationToken);

    private static ToolDiagnosticStatus MapStatus(ToolLocationStatus status) => status switch
    {
        ToolLocationStatus.Found => ToolDiagnosticStatus.Found,
        ToolLocationStatus.Missing => ToolDiagnosticStatus.Missing,
        ToolLocationStatus.SearchFailed => ToolDiagnosticStatus.SearchFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static ToolDiagnosticSource MapSource(ToolLocationSource source) => source switch
    {
        ToolLocationSource.UserSpecified => ToolDiagnosticSource.UserSpecified,
        ToolLocationSource.BundledTools => ToolDiagnosticSource.BundledTools,
        ToolLocationSource.Path => ToolDiagnosticSource.Path,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
