using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Probing;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed class Mp4FragmentSampleReader
{
    internal const int MaximumSamples = 100_000;
    internal OperationResult<Mp4NativeSampleEvidence> Read(ReadOnlyMemory<byte> moov, IReadOnlyList<Mp4MoofBody> fragments,
        IReadOnlyList<Mp4PayloadRange> payloads) => Mp4TimingReader.Convert(new LocalMp4FragmentSampleReader().Read(moov,
            fragments.Select(f => new ClipMaker.Media.Infrastructure.Probing.Mp4MoofBody(f.Offset, f.Body)).ToArray(),
            payloads.Select(p => new ClipMaker.Media.Infrastructure.Probing.Mp4PayloadRange(p.Start, p.End)).ToArray()));
}
