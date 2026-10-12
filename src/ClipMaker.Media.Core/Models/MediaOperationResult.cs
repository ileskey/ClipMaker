namespace ClipMaker.Media.Core.Models;

public enum MediaOperationStatus { Succeeded, Failed, Cancelled }
public enum MediaFailureKind
{
    MissingTool, UnsupportedInput, InvalidMedia, ProcessFailed,
    TimedOut, TerminationUnconfirmed, OutputFailed
}

public sealed record MediaFailure
{
    public MediaFailureKind Kind { get; }
    public string Message { get; }
    public string? Field { get; }

    // Callers supply a safe diagnostic, never raw tool output or authentication data.
    public MediaFailure(MediaFailureKind kind, string message, string? field = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Message = message;
        Field = field;
    }
}

public sealed class MediaOperationResult<T> where T : class
{
    public MediaOperationStatus Status { get; }
    public T? Data { get; }
    public MediaFailure? Failure { get; }

    private MediaOperationResult(MediaOperationStatus status, T? data, MediaFailure? failure)
    {
        Status = status;
        Data = data;
        Failure = failure;
    }

    public static MediaOperationResult<T> Succeeded(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new(MediaOperationStatus.Succeeded, data, null);
    }

    public static MediaOperationResult<T> Failed(MediaFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(MediaOperationStatus.Failed, null, failure);
    }

    public static MediaOperationResult<T> Cancelled() => new(MediaOperationStatus.Cancelled, null, null);
}
