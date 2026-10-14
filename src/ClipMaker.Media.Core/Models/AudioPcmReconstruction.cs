namespace ClipMaker.Media.Core.Models;

// Placement refers to values actually decoded in source order. Lattice positions
// are established from native frame PTS, never from the flattened PCM offset.
public sealed record AudioPcmFramePlacement(long DecodedFirst, long LatticeFirst, int Samples);
public sealed record AudioPcmReconstructionCounts(long WrittenSamples, long InsertedZeroSamples, long DiscardedOverlapSamples);

public sealed class AudioPcmReconstructionPlan
{
    public AudioBoundaryPlan Boundary { get; }
    public int Channels { get; }
    public long DecodedSamples { get; }
    public long TrailingDecodedPaddingSamples { get; }
    public IReadOnlyList<AudioPcmFramePlacement> Frames { get; }

    public AudioPcmReconstructionPlan(AudioBoundaryPlan boundary, int channels, long decodedSamples,
        IEnumerable<AudioPcmFramePlacement> frames, long trailingDecodedPaddingSamples = 0)
    {
        ArgumentNullException.ThrowIfNull(boundary); ArgumentNullException.ThrowIfNull(frames);
        if (channels is not (1 or 2) || decodedSamples <= 0 || trailingDecodedPaddingSamples < 0 ||
            trailingDecodedPaddingSamples >= decodedSamples) throw new ArgumentException("Verified mono/stereo decoded values are required.");
        Boundary = boundary; Channels = channels; DecodedSamples = decodedSamples; Frames = Array.AsReadOnly(frames.ToArray());
        TrailingDecodedPaddingSamples = trailingDecodedPaddingSamples;
        long decoded = 0, previous = long.MinValue;
        foreach (var frame in Frames)
        {
            if (frame is null || frame.Samples <= 0 || frame.DecodedFirst != decoded || frame.LatticeFirst < previous ||
                frame.LatticeFirst < boundary.SupportFirst || checked(frame.LatticeFirst + frame.Samples) > boundary.SupportEnd)
                throw new ArgumentException("Complete source-order PCM offsets and verified lattice support are required.");
            decoded = checked(decoded + frame.Samples); previous = frame.LatticeFirst;
        }
        if (Frames.Count == 0 || decoded != decodedSamples - trailingDecodedPaddingSamples || Frames[0].LatticeFirst != boundary.SupportFirst ||
            Frames.Max(f => checked(f.LatticeFirst + f.Samples)) != boundary.SupportEnd)
            throw new ArgumentException("Observed PCM frames must establish both support boundaries.");
    }
}
