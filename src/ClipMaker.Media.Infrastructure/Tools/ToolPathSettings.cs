using System.Collections.ObjectModel;

namespace ClipMaker.Media.Infrastructure.Tools;

public sealed class ToolPathSettings
{
    private readonly IReadOnlyDictionary<string, string?> paths;

    public ToolPathSettings(IReadOnlyDictionary<string, string?>? paths = null)
    {
        this.paths = new ReadOnlyDictionary<string, string?>(
            paths is null ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(paths, StringComparer.OrdinalIgnoreCase));
    }

    public string? GetPath(string executableName) =>
        paths.TryGetValue(executableName, out var path) ? path : null;
}
