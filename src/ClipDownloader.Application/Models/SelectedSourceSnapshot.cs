namespace ClipDownloader.Application.Models;

public enum ClipMode { Fast, Accurate }

/// <summary>An immutable local selection identity, not a server content generation.</summary>
public sealed class SelectedSourceSnapshot
{
    public Guid SnapshotId { get; }
    public VideoSourceReference Source { get; }
    public FormatSelection Selection { get; }
    public ClipMode Mode { get; }
    public TimeSpan? MetadataDuration { get; }

    private SelectedSourceSnapshot(Guid id, VideoInfo info, FormatSelection selection, ClipMode mode)
    {
        SnapshotId = id;
        Source = info.Source;
        Selection = selection;
        Mode = mode;
        MetadataDuration = info.Duration;
    }

    public static OperationResult<SelectedSourceSnapshot> Create(Guid id, VideoInfo info, FormatSelection selection, ClipMode mode)
    {
        if (id == Guid.Empty) throw new ArgumentException("Snapshot identity is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (info.State != VideoState.Completed)
            return OperationResult<SelectedSourceSnapshot>.Failed(new(OperationErrorCode.SourceUnavailable,
                OperationErrorCategory.Execution, "완료된 영상의 선택 snapshot이 필요합니다."));
        var validated = FormatSelection.Create(info, selection.RequestedQuality, selection.VideoFormat,
            selection.AudioFormat, selection.Reason, selection.Warnings, selection.RankingCriteria);
        if (validated.Status != OperationStatus.Succeeded)
            return OperationResult<SelectedSourceSnapshot>.Failed(validated.Error!);
        return OperationResult<SelectedSourceSnapshot>.Succeeded(new(id, info, validated.Data!, mode));
    }

    public bool Matches(SelectedSourceSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SnapshotId == other.SnapshotId && Source == other.Source && Mode == other.Mode &&
            MetadataDuration == other.MetadataDuration && Selection.RequestedQuality == other.Selection.RequestedQuality &&
            Selection.VideoFormat == other.Selection.VideoFormat && Selection.AudioFormat == other.Selection.AudioFormat;
    }
}
