using System.Text;
using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Sources;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;

namespace ClipDownloader.Infrastructure.YtDlp;

internal sealed class YtDlpMetadataQuery
{
    private readonly IToolExecutionDiagnostics diagnostics;
    private readonly IToolLocator locator;
    private readonly ToolPathSettings settings;
    private readonly IProcessRunner runner;
    private readonly string workingDirectory;
    internal YtDlpMetadataQuery(IToolExecutionDiagnostics diagnostics, IToolLocator locator,
        ToolPathSettings settings, IProcessRunner runner, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!Path.IsPathFullyQualified(workingDirectory)) throw new ArgumentException("Working directory must be absolute.", nameof(workingDirectory));
        this.diagnostics = diagnostics;
        this.locator = locator;
        this.settings = settings;
        this.runner = runner;
        this.workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public async Task<OperationResult<YtDlpMetadataResponse>> GetMetadataAsync(Uri url, CancellationToken cancellationToken)
    {
        var sourceResult = new SupportedVideoSourceParser().Parse(url);
        if (sourceResult.Status != OperationStatus.Succeeded) return OperationResult<YtDlpMetadataResponse>.Failed(sourceResult.Error!);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = await diagnostics.DiagnoseAsync(new(ToolDiagnosticCommand.Info), cancellationToken).ConfigureAwait(false);
            if (report.Status == OperationStatus.Cancelled) return OperationResult<YtDlpMetadataResponse>.Cancelled();
            if (report.Status == OperationStatus.Failed) return OperationResult<YtDlpMetadataResponse>.Failed(report.Error!);
            var tool = report.Data!.Tools.Single();
            if (!tool.AllRequestedChecksPassed) return OperationResult<YtDlpMetadataResponse>.Failed(tool.Error ?? Error(OperationErrorCode.ProcessFailed));
            var arguments = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-remote-components",
                "--no-js-runtimes", "--simulate", "--skip-download", "--dump-single-json", "--no-playlist", "--ignore-no-formats-error",
                "--retries", "2", "--extractor-retries", "2", "--fragment-retries", "2", "--file-access-retries", "0", "--abort-on-unavailable-fragments" };
            if (sourceResult.Data!.Platform == VideoPlatform.YouTube)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = locator.Locate(new("node.exe", settings.GetPath("node.exe")), cancellationToken);
                if (node.Status != ToolLocationStatus.Found) return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.MissingTool));
                var version = await Run(node.SelectedPath!, ["--version"], TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
                var failure = Check(version);
                if (failure is not null) return failure;
                string text;
                try { text = new UTF8Encoding(false, true).GetString(version.StandardOutput.Bytes.Span).Trim(); }
                catch (DecoderFallbackException) { return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.ProcessFailed)); }
                if (!text.StartsWith('v') || !Version.TryParse(text[1..], out var parsed) || parsed.Major < 22)
                    return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.ProcessFailed));
                arguments.AddRange(["--js-runtimes", "node:" + node.SelectedPath]);
            }
            arguments.AddRange(["--", sourceResult.Data.PageUrl.AbsoluteUri]);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Run(tool.Location.SelectedPath!, arguments, TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
            var failed = Check(result);
            if (failed is not null) return failed;
            cancellationToken.ThrowIfCancellationRequested();
            var parsedInfo = new YtDlpMetadataParser().Parse(result.StandardOutput.Bytes, sourceResult.Data);
            if (parsedInfo.Status != OperationStatus.Succeeded)
                return OperationResult<YtDlpMetadataResponse>.Failed(parsedInfo.Error!);
            return OperationResult<YtDlpMetadataResponse>.Succeeded(new(parsedInfo.Data!, result.StandardOutput.Bytes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return OperationResult<YtDlpMetadataResponse>.Cancelled(); }
    }

    private Task<ProcessResult> Run(string path, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token) =>
        runner.RunAsync(new(path, arguments, workingDirectory, ProcessOutputMode.Full, ProcessOutputMode.Tail, timeout), null, token);
    private static OperationResult<YtDlpMetadataResponse>? Check(ProcessResult result)
    {
        if (result.Status == ProcessStatus.TerminationFailed || !result.RootExitConfirmed ||
            result.DescendantExitConfirmed != true || !result.StreamsDrained)
            return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.ProcessTerminationFailed));
        if (result.Status == ProcessStatus.Cancelled) return OperationResult<YtDlpMetadataResponse>.Cancelled();
        if (result.Status == ProcessStatus.TimedOut) return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.TimedOut));
        if (result.StandardOutput.LimitExceeded) return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.InvalidMetadata));
        if (result.Status != ProcessStatus.Exited || result.ExitCode != 0) return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.ProcessFailed));
        if (!result.StandardOutput.IsComplete || !result.StandardOutput.ReachedEndOfStream || result.StandardOutput.IsTruncated)
            return OperationResult<YtDlpMetadataResponse>.Failed(Error(OperationErrorCode.InvalidMetadata));
        return null;
    }
    private static OperationError Error(OperationErrorCode code) => new(code, OperationErrorCategory.Execution,
        code switch {
            OperationErrorCode.MissingTool => "메타데이터 조회에 필요한 도구를 찾지 못했습니다.",
            OperationErrorCode.TimedOut => "메타데이터 도구 실행 제한 시간을 초과했습니다.",
            OperationErrorCode.ProcessTerminationFailed => "도구 프로세스 또는 스트림의 종료를 확인하지 못했습니다.",
            OperationErrorCode.InvalidMetadata => "메타데이터 응답이 불완전하거나 크기 상한을 초과했습니다.",
            _ => "메타데이터 도구를 정상적으로 실행하지 못했습니다."
        });
}
