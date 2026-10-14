using ClipMaker.Media.Core.ValueObjects;

namespace ClipMaker.Media.Core.Models;

public sealed record Mp4OutputSample(Mp4NativeSample Native, long Pts, long Dts);
public sealed class Mp4OutputStreamObservation
{
    public MediaStreamObservation Stream { get; }
    public Mp4TrackConfiguration Configuration { get; }
    public PresentationRange ValidPresentation { get; }
    public IReadOnlyList<Mp4OutputSample> Samples { get; }
    public long PrimingSkippedSamples { get; }
    public long DecodedTailPaddingSamples { get; }
    public Mp4OutputStreamObservation(MediaStreamObservation stream, Mp4TrackConfiguration configuration,
        PresentationRange validPresentation, IEnumerable<Mp4OutputSample> samples, long primingSkippedSamples, long decodedTailPaddingSamples)
    {
        Stream = stream; Configuration = configuration; ValidPresentation = validPresentation;
        Samples = Array.AsReadOnly(samples.ToArray()); PrimingSkippedSamples = primingSkippedSamples;
        DecodedTailPaddingSamples = decodedTailPaddingSamples;
    }
}
public sealed class Mp4OutputObservation(MediaProbeResult probe, IEnumerable<Mp4OutputStreamObservation> streams)
{
    public MediaProbeResult Probe { get; } = probe;
    public IReadOnlyList<Mp4OutputStreamObservation> Streams { get; } = Array.AsReadOnly(streams.ToArray());
}
