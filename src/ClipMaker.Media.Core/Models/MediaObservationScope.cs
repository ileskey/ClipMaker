using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

public enum MediaStreamKind { Video, Audio }
public enum MediaInspectionKind { Summary, Frames, Samples }
public enum MediaTimeBasis { Demux, Native }
public enum ObservationCompleteness { Complete, Partial }

/// <summary>Local observation identity, independent of a site or selected remote format.</summary>
public sealed record MediaStreamReference
{
    public Guid FileId { get; }
    public int StreamIndex { get; }
    public MediaStreamKind Kind { get; }

    public MediaStreamReference(Guid fileId, int streamIndex, MediaStreamKind kind)
    {
        if (fileId == Guid.Empty) throw new ArgumentException("File identity is required.", nameof(fileId));
        if (streamIndex < 0) throw new ArgumentOutOfRangeException(nameof(streamIndex));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        FileId = fileId;
        StreamIndex = streamIndex;
        Kind = kind;
    }
}

/// <summary>Completeness applies to this observation, not the entire source.</summary>
public sealed record MediaObservationScope
{
    public Guid SnapshotId { get; }
    public MediaStreamReference Stream { get; }
    public MediaInspectionKind Kind { get; }
    public MediaTimeBasis TimeBasis { get; }
    public PresentationRange? Range { get; }
    public ObservationCompleteness Completeness { get; }

    public MediaObservationScope(Guid snapshotId, MediaStreamReference stream, MediaInspectionKind kind, MediaTimeBasis timeBasis,
        PresentationRange? range, ObservationCompleteness completeness)
    {
        if (snapshotId == Guid.Empty) throw new ArgumentException("Snapshot identity is required.", nameof(snapshotId));
        ArgumentNullException.ThrowIfNull(stream);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(timeBasis)) throw new ArgumentOutOfRangeException(nameof(timeBasis));
        if (!Enum.IsDefined(completeness)) throw new ArgumentOutOfRangeException(nameof(completeness));
        if (kind != MediaInspectionKind.Summary && range is null)
            throw new ArgumentException("Frame/sample observations require a bounded range.", nameof(range));
        if (kind == MediaInspectionKind.Frames && stream.Kind != MediaStreamKind.Video ||
            kind == MediaInspectionKind.Samples && stream.Kind != MediaStreamKind.Audio)
            throw new ArgumentException("Inspection kind must match the stream.", nameof(kind));
        SnapshotId = snapshotId;
        Stream = stream;
        Kind = kind;
        TimeBasis = timeBasis;
        Range = range;
        Completeness = completeness;
    }

    public bool Covers(Guid snapshotId, MediaStreamReference stream, MediaInspectionKind kind, MediaTimeBasis timeBasis,
        PresentationRange range)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(range);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(timeBasis)) throw new ArgumentOutOfRangeException(nameof(timeBasis));
        return Completeness == ObservationCompleteness.Complete && SnapshotId == snapshotId &&
            Stream == stream && Kind == kind && TimeBasis == timeBasis && Range is not null &&
            Range.Start.CompareTo(range.Start) <= 0 && Range.End.CompareTo(range.End) >= 0;
    }
}
