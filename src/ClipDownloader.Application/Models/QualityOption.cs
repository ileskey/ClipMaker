namespace ClipDownloader.Application.Models;

public enum QualityKind { Best, Resolution }

public sealed record QualityOption
{
    public QualityKind Kind { get; }
    public int? Height { get; }
    public static QualityOption Best { get; } = new(QualityKind.Best, null);

    private QualityOption(QualityKind kind, int? height)
    {
        Kind = kind;
        Height = height;
    }

    public static bool TryCreateResolution(int height, out QualityOption? value, out OperationError? error)
    {
        value = null;
        error = null;
        if (height <= 0)
        {
            error = new(OperationErrorCode.InvalidInput, OperationErrorCategory.Input,
                "지정 해상도의 높이는 양수여야 합니다.", "Quality");
            return false;
        }
        value = new(QualityKind.Resolution, height);
        return true;
    }
}
