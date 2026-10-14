using ClipDownloader.Application.Validation;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Application.Models;

// Verified owned staging; final once-only user-file commit remains a host operation.
public sealed class VerifiedClipOutput
{
    public QualifiedClipPlan Plan { get; }
    public MediaProcessingResult Processing { get; }
    public Mp4OutputObservation Output { get; }
    public PresentationRange VideoSourcePresentation { get; }
    public PresentationRange? AudioSourcePresentation { get; }

    public VerifiedClipOutput(QualifiedClipPlan plan, MediaProcessingResult processing, Mp4OutputObservation output,
        PresentationRange videoSourcePresentation, PresentationRange? audioSourcePresentation)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(processing);
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(videoSourcePresentation);
        if (!ReferenceEquals(plan.Processing, processing.Request.Plan) || processing.Staging.Content is null ||
            output.Probe.Request.FileId != processing.Staging.Id || output.Probe.Request.Content != processing.Staging.Content ||
            !string.Equals(output.Probe.Request.Path, processing.Staging.Path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Plan, processed staging and output inspection must match.");
        Plan = plan; Processing = processing; Output = output;
        VideoSourcePresentation = videoSourcePresentation; AudioSourcePresentation = audioSourcePresentation;
    }
}
