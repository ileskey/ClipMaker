using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;

namespace ClipDownloader.ViewModels;

public sealed record ToolDiagnosticItem(string ToolName, string StatusText, string Detail)
{
    public static ToolDiagnosticItem From(ToolDiagnostic diagnostic)
    {
        var status = diagnostic.Status switch
        {
            ToolDiagnosticStatus.Found => "발견",
            ToolDiagnosticStatus.Missing => "누락",
            ToolDiagnosticStatus.SearchFailed => "탐색 실패",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnostic))
        };
        var detail = diagnostic.Status switch
        {
            ToolDiagnosticStatus.Found => $"{SourceText(diagnostic.Source)} · {diagnostic.SelectedPath}",
            ToolDiagnosticStatus.Missing when diagnostic.IsUserSpecified =>
                $"지정한 실행 파일이 없습니다. · {diagnostic.Inspections.FirstOrDefault()?.Path}",
            ToolDiagnosticStatus.Missing => "앱의 tools 폴더와 PATH에서 실행 파일을 찾지 못했습니다.",
            _ => $"{(diagnostic.IsUserSpecified ? "사용자 지정 경로" : "기본 탐색 경로")}를 확인하지 못했습니다. " +
                FriendlyReason(diagnostic)
        };
        if (diagnostic.Status == ToolDiagnosticStatus.Found &&
            diagnostic.Inspections.Any(item => item.Status == ToolDiagnosticStatus.SearchFailed))
            detail += " · 앞선 후보에서 확인 오류가 발생했습니다.";
        return new(diagnostic.ToolName, status, detail);
    }

    private static string SourceText(ToolDiagnosticSource? source) => source switch
    {
        ToolDiagnosticSource.UserSpecified => "사용자 지정",
        ToolDiagnosticSource.BundledTools => "앱 tools",
        ToolDiagnosticSource.Path => "PATH",
        _ => ""
    };

    private static string FriendlyReason(ToolDiagnostic diagnostic)
    {
        var reason = diagnostic.Inspections.FirstOrDefault(item =>
            item.Status == ToolDiagnosticStatus.SearchFailed)?.FailureReason;
        // Keep exception type, message and stack details in diagnostics/logs.
        return reason?.Split(" (", 2)[0] ?? "경로 형식과 접근 권한을 확인해주세요.";
    }
}
