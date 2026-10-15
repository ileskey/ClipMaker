using ClipMaker.Media.Core.Models;

namespace ClipMaker.Media.Infrastructure.Processing;

// Lossless reconstruction only. The caller owns output creation/capture and must
// establish decoder preparation and source coverage before passing this plan.
public sealed class AudioPcmReconstructor
{
    public async Task<AudioPcmReconstructionCounts> ReconstructAsync(AudioPcmReconstructionPlan plan, Stream decoded,
        Stream output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(decoded); ArgumentNullException.ThrowIfNull(output);
        int stride = checked(plan.Channels * 4);
        if (!decoded.CanRead || !decoded.CanSeek || !output.CanWrite || ReferenceEquals(decoded, output) ||
            decoded.Length != checked(plan.DecodedSamples * stride)) throw new InvalidDataException("PCM input layout changed.");
        long first = plan.Boundary.SelectedFirst, end = plan.Boundary.SelectedEnd, cursor = first;
        long zeros = 0, overlap = 0;
        var buffer = new byte[81920];
        foreach (var frame in plan.Frames)
        {
            token.ThrowIfCancellationRequested();
            long start = Math.Max(first, frame.LatticeFirst), finish = Math.Min(end, checked(frame.LatticeFirst + frame.Samples));
            if (finish <= start) continue;
            overlap = checked(overlap + Math.Max(0, Math.Min(cursor, finish) - start));
            if (start > cursor)
            {
                long missing = start - cursor;
                Array.Clear(buffer);
                await WriteZeros(output, checked(missing * stride), buffer, token).ConfigureAwait(false);
                zeros = checked(zeros + missing); cursor = start;
            }
            long copyStart = Math.Max(start, cursor);
            if (finish <= copyStart) continue;
            decoded.Position = checked((frame.DecodedFirst + copyStart - frame.LatticeFirst) * stride);
            long remaining = checked((finish - copyStart) * stride);
            while (remaining > 0)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await decoded.ReadExactlyAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                remaining -= count;
            }
            cursor = finish;
        }
        if (cursor != end) throw new InvalidDataException("Selected support end was not reconstructed.");
        token.ThrowIfCancellationRequested();
        return new(end - first, zeros, overlap);
    }

    private static async Task WriteZeros(Stream output, long bytes, byte[] zeros, CancellationToken token)
    {
        while (bytes > 0)
        {
            int count = (int)Math.Min(zeros.Length, bytes);
            await output.WriteAsync(zeros.AsMemory(0, count), token).ConfigureAwait(false); bytes -= count;
        }
    }
}
