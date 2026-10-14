using ClipMaker.Media.Core.Models;

namespace ClipMaker.Media.Core.Services;

public interface IMediaProbe
{
    Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(MediaProbeRequest request, CancellationToken cancellationToken = default);
}
