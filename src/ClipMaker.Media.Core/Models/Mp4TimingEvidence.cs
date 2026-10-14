using System.Numerics;

namespace ClipMaker.Media.Core.Models;

// Container observations. A track ID is not a demux stream index. An edit is
// preserved here; interpreting it as a native/movie relation is a separate gate.
public sealed record Mp4EditObservation(ulong MovieDuration, long MediaTime, short RateInteger, short RateFraction);
public sealed record Mp4TrackTiming(uint TrackId, uint MediaTimescale, bool HasEditList, IReadOnlyList<Mp4EditObservation> Edits,
    ulong? MovieDuration = null, ulong? MediaDuration = null);
public sealed record Mp4TimingObservation(uint MovieTimescale, IReadOnlyList<Mp4TrackTiming> Tracks, ulong? MovieDuration = null);
public sealed record Mp4FragmentDecodeAnchor(long MoofOffset, uint TrackId, uint HeaderFlags, ulong? BaseDecodeTime);
public sealed record Mp4TrackConfiguration(uint TrackId, string Codec, int ConfigBytes, string ConfigSha256, int? SampleRate, int? Channels);
public sealed record Mp4NativeSample(uint TrackId, BigInteger DecodeTicks, BigInteger PresentationTicks,
    uint Duration, uint Size, uint Flags, long ByteOffset);

public sealed class Mp4NativeSampleEvidence(IEnumerable<Mp4TrackConfiguration> configurations, IEnumerable<Mp4NativeSample> samples)
{
    public IReadOnlyList<Mp4TrackConfiguration> Configurations { get; } = Array.AsReadOnly(configurations.ToArray());
    public IReadOnlyList<Mp4NativeSample> Samples { get; } = Array.AsReadOnly(samples.ToArray());
}

public sealed class Mp4FileTimingEvidence
{
    public Mp4TimingObservation Initialization { get; }
    public IReadOnlyList<Mp4FragmentDecodeAnchor> FragmentAnchors { get; }
    public MediaOperationResult<Mp4NativeSampleEvidence> NativeSamples { get; }

    public Mp4FileTimingEvidence(Mp4TimingObservation initialization, IEnumerable<Mp4FragmentDecodeAnchor> anchors,
        MediaOperationResult<Mp4NativeSampleEvidence> nativeSamples)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(nativeSamples);
        Initialization = new(initialization.MovieTimescale, Array.AsReadOnly(initialization.Tracks.Select(t =>
            new Mp4TrackTiming(t.TrackId, t.MediaTimescale, t.HasEditList, Array.AsReadOnly(t.Edits.ToArray()), t.MovieDuration, t.MediaDuration)).ToArray()), initialization.MovieDuration);
        FragmentAnchors = Array.AsReadOnly(anchors.ToArray());
        NativeSamples = nativeSamples;
    }
}
