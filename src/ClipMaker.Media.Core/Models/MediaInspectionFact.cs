namespace ClipMaker.Media.Core.Models;

public enum MediaInspectionState { Unknown, Verified, Contradicted }
public enum MediaInspectionIssue
{
    NotInspected, MissingTimestamp, IncompleteObservation, ConflictingMeasurements,
    UnsupportedRepresentation, DecoderPreparationUnverified
}

/// <summary>A scoped inspection assertion; its producer must establish the evidence.</summary>
public sealed class MediaInspectionFact<T> where T : class
{
    public MediaInspectionState State { get; }
    public T? Value { get; }
    public MediaObservationScope Scope { get; }
    public MediaInspectionIssue? Issue { get; }

    private MediaInspectionFact(MediaInspectionState state, T? value, MediaObservationScope scope, MediaInspectionIssue? issue)
    {
        State = state;
        Value = value;
        Scope = scope;
        Issue = issue;
    }

    public static MediaInspectionFact<T> Verified(T value, MediaObservationScope scope)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Completeness != ObservationCompleteness.Complete)
            throw new ArgumentException("Partial observations cannot establish a verified fact.", nameof(scope));
        return new(MediaInspectionState.Verified, value, scope, null);
    }

    public static MediaInspectionFact<T> Unknown(MediaObservationScope scope, MediaInspectionIssue issue) =>
        Unresolved(MediaInspectionState.Unknown, scope, issue);

    public static MediaInspectionFact<T> Contradicted(MediaObservationScope scope, MediaInspectionIssue issue) =>
        Unresolved(MediaInspectionState.Contradicted, scope, issue);

    private static MediaInspectionFact<T> Unresolved(MediaInspectionState state, MediaObservationScope scope, MediaInspectionIssue issue)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(issue)) throw new ArgumentOutOfRangeException(nameof(issue));
        return new(state, null, scope, issue);
    }
}
