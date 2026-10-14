using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

// Exact local sample/packet/frame correspondence, limited to the observed file.
// Source origin, requested coverage, cadence and decoder preparation are separate.
public sealed record Mp4NativeStreamObservation(MediaStreamObservation Stream, Mp4TrackConfiguration Configuration,
    PresentationRange ObservedRange, long LeadingSkippedSamples = 0);

public sealed class Mp4NativeObservation(IEnumerable<Mp4NativeStreamObservation> streams)
{
    public IReadOnlyList<Mp4NativeStreamObservation> Streams { get; } = Array.AsReadOnly(streams.ToArray());
}
