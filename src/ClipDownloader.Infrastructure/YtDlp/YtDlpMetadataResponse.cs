using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.YtDlp;

// Transient raw metadata is consumed only inside Infrastructure; never log or persist it.
internal sealed class YtDlpMetadataResponse(VideoInfo info, ReadOnlyMemory<byte> json)
{
    internal VideoInfo Info { get; } = info;
    internal ReadOnlyMemory<byte> Json { get; } = json;
}
