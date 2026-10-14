namespace ClipMaker.Media.Core.Models;

public sealed class MediaAudioRenderInput
{
    public AudioBoundaryPlan Boundary { get; }
    public MediaInputFile DecodedPcm { get; }
    public MediaInputFile RenderPcm { get; }
    public AudioPcmReconstructionCounts Counts { get; }
    public MediaAudioRenderInput(AudioBoundaryPlan boundary, MediaInputFile decodedPcm, MediaInputFile renderPcm,
        AudioPcmReconstructionCounts counts, int channels)
    {
        ArgumentNullException.ThrowIfNull(boundary); ArgumentNullException.ThrowIfNull(decodedPcm);
        ArgumentNullException.ThrowIfNull(renderPcm); ArgumentNullException.ThrowIfNull(counts);
        if (channels is not (1 or 2) || counts.WrittenSamples != boundary.SelectedEnd - boundary.SelectedFirst ||
            counts.InsertedZeroSamples < 0 || counts.DiscardedOverlapSamples < 0 ||
            renderPcm.Content.Bytes != checked(counts.WrittenSamples * channels * 4) ||
            decodedPcm.Id == Guid.Empty || renderPcm.Id == Guid.Empty || decodedPcm.Id == renderPcm.Id ||
            !Path.IsPathFullyQualified(decodedPcm.Path) || !Path.IsPathFullyQualified(renderPcm.Path) ||
            string.Equals(decodedPcm.Path, renderPcm.Path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Captured lossless render values must match the selected support.");
        Boundary = boundary; DecodedPcm = decodedPcm; RenderPcm = renderPcm; Counts = counts;
    }
}
