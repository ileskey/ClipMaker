namespace ClipMaker.Media.Core.Models;

public sealed class MediaAudioDecodeRequest
{
    public Guid OperationId { get; }
    public Guid OutputFileId { get; }
    public MediaInputFile Input { get; }
    public int StreamIndex { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long ExpectedSamples { get; }
    public string PcmPath { get; }
    public MediaAudioDecodeRequest(Guid operationId, Guid outputFileId, MediaInputFile input, int streamIndex,
        int sampleRate, int channels, long expectedSamples, string pcmPath)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentException.ThrowIfNullOrWhiteSpace(pcmPath);
        if (operationId == Guid.Empty || outputFileId == Guid.Empty || input.Id == Guid.Empty || input.Content is null ||
            !Path.IsPathFullyQualified(input.Path) || !Path.IsPathFullyQualified(pcmPath) || streamIndex < 0 ||
            sampleRate is < 1 or > 65535 || channels is not (1 or 2) || expectedSamples <= 0 ||
            expectedSamples > long.MaxValue / (channels * 4)) throw new ArgumentException("Explicit captured audio input and PCM layout are required.");
        OperationId = operationId; OutputFileId = outputFileId; Input = input; StreamIndex = streamIndex;
        SampleRate = sampleRate; Channels = channels; ExpectedSamples = expectedSamples; PcmPath = Path.GetFullPath(pcmPath);
    }
}

// Captured source-order decoded values. Source-start preparation is a separate
// Downloader fact binding this result to the selected source prefix.
public sealed record MediaAudioDecodeResult(MediaAudioDecodeRequest Request, MediaOwnedProcessingFile Pcm, string FfmpegVersion);
