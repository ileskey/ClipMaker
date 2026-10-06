using System.Security;

namespace ClipMaker.Media.Infrastructure.Tools;

public sealed class ToolLocator(
    ToolSearchEnvironment environment,
    Func<string, FileAttributes>? inspectFile = null) : IToolLocator
{
    private readonly Func<string, FileAttributes> inspectFile = inspectFile ?? File.GetAttributes;

    public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExecutableName) ||
            request.ExecutableName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            request.ExecutableName is "." or "..")
            throw new ArgumentException("Executable name must be a file name.", nameof(request));

        cancellationToken.ThrowIfCancellationRequested();
        bool explicitPath = request.UserSpecifiedPath is not null;
        var inspections = new List<ToolSearchInspection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<(string Path, ToolLocationSource Source)> Candidates()
        {
            if (explicitPath)
            {
                yield return (request.UserSpecifiedPath!, ToolLocationSource.UserSpecified);
                yield break;
            }
            yield return (Path.Combine(environment.BaseDirectory, "tools", request.ExecutableName),
                ToolLocationSource.BundledTools);
            foreach (var entry in environment.PathDirectories)
            {
                // Normalize directory before combining so drive-relative PATH entries remain errors.
                yield return (entry, ToolLocationSource.Path);
            }
        }

        foreach (var candidate in Candidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = candidate.Path;
            try
            {
                path = Normalize(path);
                if (!explicitPath && candidate.Source == ToolLocationSource.Path)
                    path = Path.Combine(path, request.ExecutableName);
                if (!seen.Add(path)) continue;
                var attributes = inspectFile(path);
                cancellationToken.ThrowIfCancellationRequested();
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    inspections.Add(new(path, candidate.Source,
                        explicitPath ? ToolLocationStatus.SearchFailed : ToolLocationStatus.Missing,
                        explicitPath ? "지정한 경로가 실행 파일이 아닌 폴더입니다." : null));
                    continue;
                }
                inspections.Add(new(path, candidate.Source, ToolLocationStatus.Found, null));
                return new(ToolLocationStatus.Found, explicitPath, path, candidate.Source,
                    inspections.AsReadOnly(), null);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                inspections.Add(new(path, candidate.Source, ToolLocationStatus.Missing, null));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                or UnauthorizedAccessException or IOException or SecurityException)
            {
                inspections.Add(new(path, candidate.Source, ToolLocationStatus.SearchFailed,
                    $"{FailureMessage(ex)} ({ex.GetType().Name}: {ex.Message})"));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var failures = inspections.Where(item => item.Status == ToolLocationStatus.SearchFailed).ToArray();
        return new(failures.Length > 0 ? ToolLocationStatus.SearchFailed : ToolLocationStatus.Missing,
            explicitPath, null, null, inspections.AsReadOnly(),
            failures.Length > 0 ? string.Join(Environment.NewLine, failures.Select(item => item.FailureReason)) : null);
    }

    private string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("실행 파일 경로가 비어 있습니다.");
        if (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path))
            throw new ArgumentException("드라이브 또는 루트 상대 경로는 사용할 수 없습니다.");
        return Path.GetFullPath(path, environment.BaseDirectory);
    }

    private static string FailureMessage(Exception ex) => ex switch
    {
        UnauthorizedAccessException or SecurityException => "경로에 접근할 수 없습니다.",
        ArgumentException or NotSupportedException => "경로 형식이 올바르지 않습니다.",
        _ => "파일 정보를 확인할 수 없습니다."
    };
}
