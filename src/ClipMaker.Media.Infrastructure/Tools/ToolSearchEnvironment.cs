namespace ClipMaker.Media.Infrastructure.Tools;

public sealed class ToolSearchEnvironment
{
    public string BaseDirectory { get; }
    public IReadOnlyList<string> PathDirectories { get; }

    public ToolSearchEnvironment(string baseDirectory, IEnumerable<string> pathDirectories)
    {
        if (!Path.IsPathFullyQualified(baseDirectory))
            throw new ArgumentException("Base directory must be absolute.", nameof(baseDirectory));
        BaseDirectory = Path.GetFullPath(baseDirectory);
        PathDirectories = Array.AsReadOnly(pathDirectories
            .Select(entry => entry.Trim())
            .Select(entry => entry.Length >= 2 && entry[0] == '"' && entry[^1] == '"'
                ? entry[1..^1].Trim() : entry)
            .Where(entry => entry.Length > 0).ToArray());
    }
}
