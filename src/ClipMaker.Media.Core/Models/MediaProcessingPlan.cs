using ClipMaker.Media.Core.ValueObjects;
using System.Numerics;

namespace ClipMaker.Media.Core.Models;

public enum MediaProcessingMode { Fast, Accurate }
public sealed record MediaInputFile(Guid Id, string Path, MediaFileContentIdentity Content);
public sealed record MediaVideoFrameExpectation(long InputPts, long Duration, SignedMediaTime InputTimeBase,
    SignedMediaTime OutputPts, SignedMediaTime OutputDuration);

public sealed class AudioBoundaryPlan
{
    public AudioSampleLattice Lattice { get; }
    public long RequestedFirst { get; }
    public long RequestedEnd { get; }
    public long SupportFirst { get; }
    public long SupportEnd { get; }
    public long SelectedFirst => Math.Max(RequestedFirst, SupportFirst);
    public long SelectedEnd => Math.Min(RequestedEnd, SupportEnd);
    public SignedMediaTime RenderOrigin => Lattice.GetSampleTime(RequestedFirst);
    public SignedMediaTime Residual { get; }

    public AudioBoundaryPlan(AudioSampleLattice lattice, long requestedFirst, long requestedEnd,
        long supportFirst, long supportEnd, SignedMediaTime requestedStart)
    {
        ArgumentNullException.ThrowIfNull(lattice);
        ArgumentNullException.ThrowIfNull(requestedStart);
        if (requestedEnd <= requestedFirst || supportEnd <= supportFirst ||
            Math.Min(requestedEnd, supportEnd) <= Math.Max(requestedFirst, supportFirst))
            throw new ArgumentException("A nonempty intersection of requested samples and valid support is required.");
        Lattice = lattice; RequestedFirst = requestedFirst; RequestedEnd = requestedEnd;
        SupportFirst = supportFirst; SupportEnd = supportEnd;
        Residual = RenderOrigin - requestedStart;
        SignedMediaTime.TryCreate(1, lattice.SampleRate, out var step, out _);
        if (Residual.Numerator < 0 || Residual.CompareTo(step) >= 0)
            throw new ArgumentException("Render origin must be the requested ceil sample boundary.", nameof(requestedStart));
    }
}

// A producer must establish decoder state from actual source-start decoding and
// retain its lossless decoded values. Packet counts or a preroll length are not evidence.
public sealed record AudioDecoderPreparation(Guid SourceStartFileId, MediaFileContentIdentity SourceStartContent,
    MediaInputFile DecodedPcm, int SampleRate, int Channels, long Samples);

public sealed record MediaRandomAccessPoint(NativeTimestamp Timestamp, string PicturePayloadSha256);

public sealed class StreamProcessingPlan
{
    public MediaStreamReference Stream { get; }
    public PresentationRange InputRange { get; }
    public SignedMediaTime Translation { get; }
    public string Codec { get; }
    public SignedMediaTime InputTimeBase { get; }
    public BigInteger MinimumInputTimestamp { get; }
    public BigInteger MaximumInputTimestamp { get; }
    public int? SampleRate { get; }
    public int? Channels { get; }
    public StreamProcessingPlan(MediaStreamReference stream, PresentationRange inputRange, SignedMediaTime translation, string codec,
        SignedMediaTime inputTimeBase, BigInteger minimumInputTimestamp, BigInteger maximumInputTimestamp, int? sampleRate = null, int? channels = null)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(inputRange);
        ArgumentNullException.ThrowIfNull(translation); ArgumentException.ThrowIfNullOrWhiteSpace(codec);
        ArgumentNullException.ThrowIfNull(inputTimeBase);
        if (inputTimeBase.Numerator <= 0 || maximumInputTimestamp < minimumInputTimestamp || sampleRate <= 0 || channels <= 0)
            throw new ArgumentException("A positive native clock and ordered observed timestamp bounds are required.");
        Stream = stream; InputRange = inputRange; Translation = translation; Codec = codec;
        InputTimeBase = inputTimeBase; MinimumInputTimestamp = minimumInputTimestamp; MaximumInputTimestamp = maximumInputTimestamp;
        SampleRate = sampleRate; Channels = channels;
    }
}

public sealed class MediaProcessingPlan
{
    public MediaProcessingMode Mode { get; }
    public IReadOnlyList<MediaInputFile> Inputs { get; }
    public IReadOnlyList<StreamProcessingPlan> Streams { get; }
    public IReadOnlyList<MediaVideoFrameExpectation> RequiredVideoFrames { get; }
    public int MovieTimescale { get; }
    public AudioBoundaryPlan? AudioBoundary { get; }
    public MediaInspectionFact<AudioDecoderPreparation>? DecoderPreparation { get; }

    public MediaProcessingPlan(MediaProcessingMode mode, IEnumerable<MediaInputFile> inputs,
        IEnumerable<StreamProcessingPlan> streams, IEnumerable<MediaVideoFrameExpectation> requiredVideoFrames,
        int movieTimescale, AudioBoundaryPlan? audioBoundary = null, MediaInspectionFact<AudioDecoderPreparation>? decoderPreparation = null)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(requiredVideoFrames);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (movieTimescale <= 0) throw new ArgumentOutOfRangeException(nameof(movieTimescale));
        Mode = mode; Inputs = Array.AsReadOnly(inputs.ToArray()); Streams = Array.AsReadOnly(streams.ToArray());
        RequiredVideoFrames = Array.AsReadOnly(requiredVideoFrames.ToArray()); MovieTimescale = movieTimescale;
        if (Inputs.Count == 0 || Inputs.Any(f => f is null || f.Id == Guid.Empty || f.Content is null || !Path.IsPathFullyQualified(f.Path)) ||
            Inputs.Select(f => f.Id).Distinct().Count() != Inputs.Count || Streams.Count == 0 ||
            Streams.Any(s => s is null || !Inputs.Any(f => f.Id == s.Stream.FileId)) ||
            Streams.Select(s => s.Stream).Distinct().Count() != Streams.Count)
            throw new ArgumentException("Local input identities and unique stream plans are required.");
        if (RequiredVideoFrames.Count == 0 || RequiredVideoFrames.Any(f => f is null || f.Duration <= 0 ||
            f.InputTimeBase is null || f.InputTimeBase.Numerator <= 0 || f.OutputPts is null || f.OutputPts.Numerator < 0 ||
            f.OutputDuration is null || f.OutputDuration.Numerator <= 0))
            throw new ArgumentException("Nonempty, explicit video presentation expectations are required.", nameof(requiredVideoFrames));
        AudioBoundary = audioBoundary; DecoderPreparation = decoderPreparation;
    }
}
