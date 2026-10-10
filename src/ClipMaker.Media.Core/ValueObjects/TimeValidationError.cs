namespace ClipMaker.Media.Core.ValueObjects;

public enum TimeValidationErrorCode { InvalidSyntax, OutOfRange, Overflow }

public sealed record TimeValidationError(
    TimeValidationErrorCode Code, string Field, string Message);
