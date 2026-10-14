using ClipMaker.Media.Core.Models;

namespace ClipDownloader.Application.Models;

public sealed record OwnedMediaFile
{
    public Guid Id { get; }
    public string Path { get; }
    public IReadOnlyList<string> SourceFormatIds { get; }
    public MediaFileContentIdentity? Content { get; }
    public bool MayBeInUse { get; }
    public OwnedMediaFile(Guid id, string path, IEnumerable<string> sourceFormatIds, MediaFileContentIdentity? content = null, bool mayBeInUse = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("File identity is required.", nameof(id));
        Id = id;
        Path = AcquisitionContractValidation.AbsolutePath(path);
        ArgumentNullException.ThrowIfNull(sourceFormatIds);
        var ids = sourceFormatIds.ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Unique source format identities are required.", nameof(sourceFormatIds));
        SourceFormatIds = Array.AsReadOnly(ids);
        Content = content;
        MayBeInUse = mayBeInUse;
    }
}

// Structural ownership checks only. Workspace/adapter code establishes actual file ownership.
internal static class AcquisitionContractValidation
{
    internal static string AbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute path is required.", nameof(path));
        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }

    internal static IReadOnlyList<OwnedMediaFile> Files(string directory, IEnumerable<OwnedMediaFile> files, SelectedSourceSnapshot selection)
    {
        ArgumentNullException.ThrowIfNull(files);
        var items = files.ToArray();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (items.Length == 0 || items.Any(file => file is null) || items.Select(file => file.Id).Distinct().Count() != items.Length ||
            items.Select(file => file.Path).Distinct(comparer).Count() != items.Length)
            throw new ArgumentException("Unique owned files are required.", nameof(files));
        foreach (var file in items)
        {
            if (file.SourceFormatIds.Any(id => id != selection.Selection.VideoFormat.Id && id != selection.Selection.AudioFormat?.Id))
                throw new ArgumentException("File provenance refers to an unselected format.", nameof(files));
            string relative = System.IO.Path.GetRelativePath(directory, file.Path);
            if (relative == "." || relative == ".." || System.IO.Path.IsPathRooted(relative) ||
                relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Owned files must be below the work directory.", nameof(files));
        }
        return Array.AsReadOnly(items);
    }

    internal static void Fact<T>(MediaInspectionFact<T> fact, Guid snapshot, IReadOnlyList<OwnedMediaFile> files,
        SelectedSourceSnapshot selection, MediaTimeBasis? basis = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(fact);
        string format = fact.Scope.Stream.Kind == MediaStreamKind.Video ? selection.Selection.VideoFormat.Id :
            (selection.Selection.AudioFormat ?? selection.Selection.VideoFormat).Id;
        if (fact.Scope.SnapshotId != snapshot || !files.Any(file => file.Id == fact.Scope.Stream.FileId && file.SourceFormatIds.Contains(format)) ||
            basis is not null && fact.Scope.TimeBasis != basis)
            throw new ArgumentException("Inspection snapshot, file identity or time basis does not match.", nameof(fact));
    }

    internal static bool SameDirectory(string left, string right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
