using System.Text;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.Diagnostics;

public sealed class ToolExecutionDiagnostics : IToolExecutionDiagnostics
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IToolLocator locator;
    private readonly ToolPathSettings settings;
    private readonly IProcessRunner runner;
    private readonly string workingDirectory;

    public ToolExecutionDiagnostics(IToolLocator locator, ToolPathSettings settings, IProcessRunner runner,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!Path.IsPathFullyQualified(workingDirectory))
            throw new ArgumentException("Diagnostic working directory must be absolute.", nameof(workingDirectory));
        this.locator = locator;
        this.settings = settings;
        this.runner = runner;
        this.workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public async Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(
        ToolDiagnosticRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<ToolExecutionDiagnostic>();
        ToolKind[] tools = request.Command == ToolDiagnosticCommand.Info
            ? [ToolKind.YtDlp] : [ToolKind.YtDlp, ToolKind.Ffmpeg, ToolKind.Ffprobe];
        try
        {
            foreach (ToolKind tool in tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = ExecutableName(tool);
                var found = await Task.Run(() => locator.Locate(new(name, settings.GetPath(name)), cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                ToolDiagnostic location = MapLocation(name, found);
                var features = (tool == ToolKind.Ffmpeg ? request.RequiredFeatures : [])
                    .Select(requirement => new ToolFeatureCheck(requirement, ToolCheckStatus.NotChecked, null)).ToList();
                if (found.Status != ToolLocationStatus.Found)
                {
                    OperationError error = Error(found.Status == ToolLocationStatus.Missing ? OperationErrorCode.MissingTool :
                        OperationErrorCode.ProcessFailed, "필요한 도구 경로를 확인하지 못했습니다.");
                    diagnostics.Add(new(tool, location, ToolCheckStatus.NotChecked, ToolCheckStatus.NotChecked, null, null, error, features));
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                ProcessResult versionResult = await ExecuteAsync(found.SelectedPath!, tool == ToolKind.YtDlp
                    ? ["--ignore-config", "--version"] : ["-version"], cancellationToken).ConfigureAwait(false);
                var interrupted = Interrupted(versionResult);
                if (interrupted is not null) return interrupted;
                OperationError? executionError = ProcessError(versionResult);
                if (executionError is not null)
                {
                    diagnostics.Add(new(tool, location, ToolCheckStatus.Failed, ToolCheckStatus.NotChecked,
                        null, versionResult.ExitCode, executionError, features));
                    continue;
                }
                string? version = TryText(versionResult.StandardOutput) is { } text ? ToolOutputParser.ReadVersion(tool, text) : null;
                if (version is null)
                {
                    diagnostics.Add(new(tool, location, ToolCheckStatus.Passed, ToolCheckStatus.Failed,
                        null, versionResult.ExitCode, Error(OperationErrorCode.ProcessFailed, "도구 버전 응답을 확인하지 못했습니다."), features));
                    continue;
                }

                foreach (ToolFeatureKind kind in request.RequiredFeatures.Select(feature => feature.Kind).Distinct())
                {
                    if (tool != ToolKind.Ffmpeg) break;
                    cancellationToken.ThrowIfCancellationRequested();
                    ProcessResult listing = await ExecuteAsync(found.SelectedPath!,
                        ["-hide_banner", kind == ToolFeatureKind.Encoder ? "-encoders" : "-muxers"], cancellationToken).ConfigureAwait(false);
                    interrupted = Interrupted(listing);
                    if (interrupted is not null) return interrupted;
                    OperationError? listingError = ProcessError(listing);
                    HashSet<string>? names = listingError is null && TryText(listing.StandardOutput) is { } listingText
                        ? ToolOutputParser.ReadFeatures(kind, listingText) : null;
                    if (names is null && listingError is null)
                        listingError = Error(OperationErrorCode.ProcessFailed, "도구 기능 목록을 확인하지 못했습니다.");
                    for (int index = 0; index < features.Count; index++)
                    {
                        var requirement = features[index].Requirement;
                        if (requirement.Kind != kind) continue;
                        bool passed = names?.Contains(requirement.Name) == true;
                        features[index] = new(requirement, passed ? ToolCheckStatus.Passed : ToolCheckStatus.Failed,
                            passed ? null : listingError ?? Error(OperationErrorCode.UnsupportedFormat, "요청한 미디어 기능을 도구가 제공하지 않습니다."));
                    }
                }
                diagnostics.Add(new(tool, location, ToolCheckStatus.Passed, ToolCheckStatus.Passed,
                    version, versionResult.ExitCode, null, features));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return OperationResult<ToolExecutionReport>.Succeeded(new(request.Command, diagnostics));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult<ToolExecutionReport>.Cancelled();
        }
    }

    private Task<ProcessResult> ExecuteAsync(string path, string[] arguments, CancellationToken token) =>
        runner.RunAsync(new(path, arguments, workingDirectory, ProcessOutputMode.Full, ProcessOutputMode.Tail, ProbeTimeout), null, token);

    private static OperationResult<ToolExecutionReport>? Interrupted(ProcessResult result)
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed ||
            result.DescendantExitConfirmed != true || !result.StreamsDrained)
            return OperationResult<ToolExecutionReport>.Failed(
                Error(OperationErrorCode.ProcessTerminationFailed, "도구 프로세스 또는 스트림의 종료를 확인하지 못했습니다."));
        return result.Status == ProcessStatus.Cancelled ? OperationResult<ToolExecutionReport>.Cancelled() : null;
    }

    private static OperationError? ProcessError(ProcessResult result)
    {
        if (result.Status == ProcessStatus.TimedOut) return Error(OperationErrorCode.TimedOut, "도구 진단 제한 시간 15초를 초과했습니다.");
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0)
            return Error(OperationErrorCode.ProcessFailed, "도구 진단 명령을 정상적으로 실행하지 못했습니다.");
        if (!result.RootExitConfirmed || result.DescendantExitConfirmed != true || !result.StreamsDrained)
            return Error(OperationErrorCode.ProcessTerminationFailed, "도구 진단의 종료 정보를 확인하지 못했습니다.");
        return null;
    }

    private static string? TryText(ProcessOutput output)
    {
        if (!output.IsComplete || !output.ReachedEndOfStream || output.IsTruncated || output.LimitExceeded) return null;
        try { return StrictUtf8.GetString(output.Bytes.Span); }
        catch (DecoderFallbackException) { return null; }
    }

    private static OperationError Error(OperationErrorCode code, string message) => new(code, OperationErrorCategory.Execution, message);
    private static string ExecutableName(ToolKind tool) => tool switch
    {
        ToolKind.YtDlp => "yt-dlp.exe", ToolKind.Ffmpeg => "ffmpeg.exe", ToolKind.Ffprobe => "ffprobe.exe",
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    private static ToolDiagnostic MapLocation(string name, ToolLocationResult result) => new(name,
        MapStatus(result.Status), result.IsUserSpecified, result.SelectedPath,
        result.Source is { } source ? MapSource(source) : null,
        Array.AsReadOnly(result.Inspections.Select(item => new ToolDiagnosticInspection(item.Path,
            MapSource(item.Source), MapStatus(item.Status),
            item.FailureReason is null ? null : "후보 경로 검사에 실패했습니다.")).ToArray()),
        result.FailureReason is null ? null : "도구 경로 검사에 실패했습니다.");

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
