namespace ClipDownloader.Application.Models;

public enum ToolDiagnosticCommand { Diagnose, Info, Clip }
public enum ToolKind { YtDlp, Ffmpeg, Ffprobe }
public enum ToolCheckStatus { NotChecked, Passed, Failed }
public enum ToolFeatureKind { Encoder, Muxer }

public sealed record ToolFeatureRequirement
{
    public ToolFeatureKind Kind { get; }
    public string Name { get; }
    public ToolFeatureRequirement(ToolFeatureKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (name.Length is 0 or > 128 || name.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.' and not '+'))
            throw new ArgumentException("Feature name must be an exact tool identifier.", nameof(name));
        Kind = kind;
        Name = name;
    }
}

public sealed class ToolDiagnosticRequest
{
    public ToolDiagnosticCommand Command { get; }
    public IReadOnlyList<ToolFeatureRequirement> RequiredFeatures { get; }
    public ToolDiagnosticRequest(ToolDiagnosticCommand command, IEnumerable<ToolFeatureRequirement>? requiredFeatures = null)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        ToolFeatureRequirement[] features = requiredFeatures?.ToArray() ?? [];
        if (features.Any(feature => feature is null)) throw new ArgumentException("Features must not contain null.", nameof(requiredFeatures));
        if (command != ToolDiagnosticCommand.Clip && features.Length != 0)
            throw new ArgumentException("Only clip diagnoses requested media features.", nameof(requiredFeatures));
        Command = command;
        RequiredFeatures = Array.AsReadOnly(features.Distinct().ToArray());
    }
}

// Passed means declared in the tool's feature listing, not a successful media encode.
public sealed record ToolFeatureCheck(ToolFeatureRequirement Requirement, ToolCheckStatus Status, OperationError? Error);

public sealed class ToolExecutionDiagnostic
{
    public ToolKind Tool { get; }
    public ToolDiagnostic Location { get; }
    public ToolCheckStatus ExecutionStatus { get; }
    public ToolCheckStatus VersionStatus { get; }
    public string? Version { get; }
    public int? VersionExitCode { get; }
    public OperationError? Error { get; }
    public IReadOnlyList<ToolFeatureCheck> Features { get; }
    public bool AllRequestedChecksPassed => Location.Status == ToolDiagnosticStatus.Found &&
        ExecutionStatus == ToolCheckStatus.Passed && VersionStatus == ToolCheckStatus.Passed && Version is not null &&
        Features.All(feature => feature.Status == ToolCheckStatus.Passed);

    public ToolExecutionDiagnostic(ToolKind tool, ToolDiagnostic location, ToolCheckStatus executionStatus,
        ToolCheckStatus versionStatus, string? version, int? versionExitCode, OperationError? error,
        IEnumerable<ToolFeatureCheck> features)
    {
        Tool = tool;
        Location = location with { Inspections = Array.AsReadOnly(location.Inspections.ToArray()) };
        ExecutionStatus = executionStatus;
        VersionStatus = versionStatus;
        Version = version;
        VersionExitCode = versionExitCode;
        Error = error;
        Features = Array.AsReadOnly(features.ToArray());
    }
}

public sealed class ToolExecutionReport
{
    public ToolDiagnosticCommand Command { get; }
    public IReadOnlyList<ToolExecutionDiagnostic> Tools { get; }
    public bool AllRequestedChecksPassed => Tools.All(tool => tool.AllRequestedChecksPassed);
    public ToolExecutionReport(ToolDiagnosticCommand command, IEnumerable<ToolExecutionDiagnostic> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        ToolExecutionDiagnostic[] snapshot = tools.ToArray();
        ToolKind[] expected = command == ToolDiagnosticCommand.Info ? [ToolKind.YtDlp] : [ToolKind.YtDlp, ToolKind.Ffmpeg, ToolKind.Ffprobe];
        if (snapshot.Any(tool => tool is null) || snapshot.Length != expected.Length ||
            !expected.ToHashSet().SetEquals(snapshot.Select(tool => tool.Tool)))
            throw new ArgumentException("Report must include each requested tool exactly once.", nameof(tools));
        Command = command;
        Tools = Array.AsReadOnly(snapshot);
    }
}
