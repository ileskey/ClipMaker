using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.FileSystem;

public sealed class OutputPathValidator : IOutputPathValidator
{
    private readonly string currentDirectory;
    public OutputPathValidator(string currentDirectory) => this.currentDirectory = Path.GetFullPath(currentDirectory);
    public OperationResult<string> Validate(string outputPath, OperationErrorCategory category = OperationErrorCategory.Input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(outputPath) || Path.EndsInDirectorySeparator(outputPath)) return Fail(OperationErrorCode.InvalidOutputPath, category);
            // Windows normalization may silently strip these suffixes. Reject
            // the requested spelling before obtaining a canonical path.
            if (outputPath.EndsWith(' ') || outputPath.EndsWith('.')) return Fail(OperationErrorCode.InvalidOutputPath, category);
            string path = Path.GetFullPath(outputPath, currentDirectory), name = Path.GetFileName(path);
            string stem = name.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
            bool reserved = stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                    "123456789¹²³".Contains(stem[3]);
            if (name.Length == 0 || name.EndsWith(' ') || name.EndsWith('.') || reserved ||
                name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) || !string.Equals(Path.GetExtension(name), ".mp4", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(Path.GetDirectoryName(path))) return Fail(OperationErrorCode.InvalidOutputPath, category);
            if (File.Exists(path) || Directory.Exists(path)) return Fail(OperationErrorCode.OutputConflict, category);
            return OperationResult<string>.Succeeded(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { return Fail(OperationErrorCode.InvalidOutputPath, category); }
    }
    private static OperationResult<string> Fail(OperationErrorCode code, OperationErrorCategory category) => OperationResult<string>.Failed(
        new(code, category, code == OperationErrorCode.OutputConflict ? "출력 경로가 이미 존재합니다." : "기존 폴더의 유효한 MP4 출력 경로가 필요합니다.", "Output"));
}
