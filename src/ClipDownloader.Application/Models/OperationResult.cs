namespace ClipDownloader.Application.Models;

public enum OperationStatus { Succeeded, Failed, Cancelled }
public enum OperationErrorCategory { Input, Execution }
public enum OperationErrorCode
{
    InvalidInput, UnsupportedSource, InvalidRange, InvalidOutputPath, OutputConflict,
    MissingTool, SourceUnavailable, InvalidMetadata, UnsupportedFormat, UnknownFrameRate,
    ProcessFailed, TimedOut, ProcessTerminationFailed, OutputFailed, VerificationFailed
}

public sealed record OperationError(
    OperationErrorCode Code, OperationErrorCategory Category, string Message, string? Field = null);

public sealed class OperationResult<T> where T : class
{
    public OperationStatus Status { get; }
    public T? Data { get; }
    public OperationError? Error { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> RemainingTemporaryPaths { get; }
    private OperationResult(OperationStatus status, T? data, OperationError? error, IEnumerable<string>? warnings = null,
        IEnumerable<string>? remainingTemporaryPaths = null)
    {
        Status = status;
        Data = data;
        Error = error;
        Warnings = Array.AsReadOnly(warnings?.ToArray() ?? []);
        RemainingTemporaryPaths = Array.AsReadOnly(remainingTemporaryPaths?.ToArray() ?? []);
    }
    public static OperationResult<T> Succeeded(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(OperationStatus.Succeeded, data, null);
    }
    public static OperationResult<T> Failed(OperationError error, IEnumerable<string>? warnings = null, IEnumerable<string>? remainingTemporaryPaths = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(OperationStatus.Failed, null, error, warnings, remainingTemporaryPaths);
    }
    public static OperationResult<T> Cancelled() => new(OperationStatus.Cancelled, null, null);
}
