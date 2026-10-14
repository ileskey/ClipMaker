using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

public enum MediaProbeDetail { Summary, FramesAndPackets }

public sealed class MediaProbeRequest
{
    public Guid SnapshotId { get; }
    public Guid FileId { get; }
    public string Path { get; }
    public MediaFileContentIdentity Content { get; }
    public MediaProbeDetail Detail { get; }
    // Requested presentation range is a qualification input, not a seek instruction.
    // The bounded owned file is observed in full, retaining preparation and reorder evidence.
    public PresentationRange? Range { get; }

    public MediaProbeRequest(Guid snapshotId, Guid fileId, string path, MediaFileContentIdentity content,
        MediaProbeDetail detail, PresentationRange? range = null)
    {
        if (snapshotId == Guid.Empty || fileId == Guid.Empty) throw new ArgumentException("Local snapshot/file identities are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute local file path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(detail)) throw new ArgumentOutOfRangeException(nameof(detail));
        if (detail == MediaProbeDetail.FramesAndPackets && range is null) throw new ArgumentException("Detailed inspection requires a bounded requested range.", nameof(range));
        SnapshotId = snapshotId; FileId = fileId; Path = System.IO.Path.GetFullPath(path);
        Content = content; Detail = detail; Range = range;
    }
}

public sealed class MediaProbeResult
{
    public MediaProbeRequest Request { get; }
    public string ProbeVersion { get; }
    public MediaProbeObservations Observations { get; }

    public MediaProbeResult(MediaProbeRequest request, string probeVersion, MediaProbeObservations observations)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(probeVersion);
        ArgumentNullException.ThrowIfNull(observations);
        if (request.Detail == MediaProbeDetail.FramesAndPackets && (!observations.FramesObserved || !observations.PacketsObserved))
            throw new ArgumentException("Detailed probe output must include both observation sets.", nameof(observations));
        Request = request; ProbeVersion = probeVersion; Observations = observations;
    }
}
