namespace ClipDownloader.Application.Models;

public enum ToolDiagnosticStatus { Found, Missing, SearchFailed }
public enum ToolDiagnosticSource { UserSpecified, BundledTools, Path }

public sealed record ToolDiagnosticInspection(
    string Path, ToolDiagnosticSource Source, ToolDiagnosticStatus Status, string? FailureReason);

public sealed record ToolDiagnostic(
    string ToolName, ToolDiagnosticStatus Status, bool IsUserSpecified,
    string? SelectedPath, ToolDiagnosticSource? Source,
    IReadOnlyList<ToolDiagnosticInspection> Inspections, string? FailureReason);
