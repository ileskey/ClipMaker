namespace ClipMaker.Media.Infrastructure.Tools;

public sealed record ToolSearchInspection(
    string Path, ToolLocationSource Source, ToolLocationStatus Status, string? FailureReason);

public sealed record ToolLocationResult(
    ToolLocationStatus Status,
    bool IsUserSpecified,
    string? SelectedPath,
    ToolLocationSource? Source,
    IReadOnlyList<ToolSearchInspection> Inspections,
    string? FailureReason);
