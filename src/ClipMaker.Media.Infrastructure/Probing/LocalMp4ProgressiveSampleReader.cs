using System.Buffers.Binary;
using System.Numerics;
using ClipMaker.Media.Core.Models;
using static ClipMaker.Media.Infrastructure.Probing.LocalMp4TimingReader;

namespace ClipMaker.Media.Infrastructure.Probing;

// Independent progressive output tables. This observes raw coded timing, not
// edited valid support, decoder history, or the source strategy's supported edits.
internal sealed class LocalMp4ProgressiveSampleReader
{
    private const int MaximumSamples = 100_000;
    internal MediaOperationResult<Mp4NativeSampleEvidence> Read(ReadOnlyMemory<byte> moov, IReadOnlyList<Mp4PayloadRange> payloads)
    {
        try
        {
            int count = 0;
            var movie = Boxes(moov, ref count);
            if (movie.Any(b => b.Type == "mvex")) throw new Unsupported();
            var configs = new List<Mp4TrackConfiguration>(); var samples = new List<Mp4NativeSample>();
            foreach (var track in movie.Where(b => b.Type == "trak"))
            {
                if (configs.Count == 16) throw new Unsupported();
                var children = Boxes(track.Body, ref count); var header = Single(children, "tkhd").Body;
                uint id = U32(header, Version(header) == 0 ? 12 : 20);
                if (id == 0 || configs.Any(c => c.TrackId == id)) throw new InvalidDataException();
                var media = Boxes(Single(children, "mdia").Body, ref count);
                var minf = Boxes(Single(media, "minf").Body, ref count);
                var table = Boxes(Single(minf, "stbl").Body, ref count);
                var description = Single(table, "stsd").Body;
                if (Version(description) != 0 || U32(description, 4) != 1) throw new Unsupported();
                var entries = Boxes(description[8..], ref count);
                if (entries.Count != 1) throw new InvalidDataException();
                configs.Add(LocalMp4FragmentSampleReader.Configuration(id, entries[0], ref count, allowDisabledSbrExtension: true));
                var sizes = Sizes(Single(table, "stsz").Body);
                var durations = Runs(Single(table, "stts").Body, sizes.Length, composition: false);
                var ctts = table.Where(b => b.Type == "ctts").ToArray();
                if (ctts.Length > 1) throw new InvalidDataException();
                var offsets = ctts.Length == 0 ? new long[sizes.Length] : Runs(ctts[0].Body, sizes.Length, composition: true);
                var syncTables = table.Where(b => b.Type == "stss").ToArray();
                if (syncTables.Length > 1) throw new InvalidDataException();
                HashSet<uint>? sync = null;
                if (syncTables.Length == 1)
                {
                    var body = syncTables[0].Body; uint n = Entries(body, 4); sync = [];
                    uint previous = 0;
                    for (int i = 0; i < n; i++)
                    {
                        uint sample = U32(body, 8 + i * 4);
                        if (sample <= previous || sample > sizes.Length) throw new InvalidDataException();
                        sync.Add(sample); previous = sample;
                    }
                }
                var chunks = table.Where(b => b.Type is "stco" or "co64").ToArray();
                if (chunks.Length != 1) throw new InvalidDataException();
                int width = chunks[0].Type == "stco" ? 4 : 8;
                uint chunkCount = Entries(chunks[0].Body, width);
                if (chunkCount == 0) throw new InvalidDataException();
                var layout = Single(table, "stsc").Body; uint runCount = Entries(layout, 12);
                if (runCount == 0) throw new InvalidDataException();
                var runs = new List<(uint First, uint Samples)>();
                for (int i = 0; i < runCount; i++)
                {
                    uint first = U32(layout, 8 + i * 12), per = U32(layout, 12 + i * 12), desc = U32(layout, 16 + i * 12);
                    if (per == 0 || desc != 1 || first > chunkCount || i == 0 && first != 1 ||
                        i > 0 && first <= runs[^1].First) throw new InvalidDataException();
                    runs.Add((first, per));
                }
                int sampleIndex = 0, runIndex = 0; BigInteger decode = 0;
                for (uint chunk = 1; chunk <= chunkCount; chunk++)
                {
                    while (runIndex + 1 < runs.Count && runs[runIndex + 1].First <= chunk) runIndex++;
                    int at = checked(8 + (int)(chunk - 1) * width);
                    ulong raw = width == 4 ? U32(chunks[0].Body, at) : BinaryPrimitives.ReadUInt64BigEndian(chunks[0].Body.Span[at..]);
                    if (raw > long.MaxValue) throw new Unsupported();
                    long position = (long)raw;
                    for (uint i = 0; i < runs[runIndex].Samples; i++)
                    {
                        if (sampleIndex == sizes.Length || samples.Count == MaximumSamples) throw new InvalidDataException();
                        uint size = sizes[sampleIndex]; long end = checked(position + size);
                        if (!payloads.Any(p => position >= p.Start && end <= p.End)) throw new InvalidDataException();
                        uint flags = sync is null || sync.Contains((uint)sampleIndex + 1) ? 0u : 0x01010000u;
                        samples.Add(new(id, decode, decode + offsets[sampleIndex], checked((uint)durations[sampleIndex]), size, flags, position));
                        decode += durations[sampleIndex]; position = end; sampleIndex++;
                    }
                }
                if (sampleIndex != sizes.Length) throw new InvalidDataException();
            }
            if (samples.Count == 0) throw new InvalidDataException();
            var physical = samples.OrderBy(s => s.ByteOffset).ToArray();
            for (int i = 1; i < physical.Length; i++)
                if (physical[i - 1].ByteOffset + physical[i - 1].Size > physical[i].ByteOffset) throw new InvalidDataException();
            return MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(new(configs, samples));
        }
        catch (Unsupported) { return Fail(MediaFailureKind.UnsupportedInput); }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        { return Fail(MediaFailureKind.InvalidMedia); }
    }

    private static uint Entries(ReadOnlyMemory<byte> body, int width)
    {
        if (Version(body) != 0) throw new Unsupported();
        uint count = U32(body, 4);
        if (count > MaximumSamples || body.Length != 8L + count * width) throw new InvalidDataException();
        return count;
    }
    private static uint[] Sizes(ReadOnlyMemory<byte> body)
    {
        if (Version(body) != 0) throw new Unsupported();
        uint fixedSize = U32(body, 4), count = U32(body, 8);
        if (count is 0 or > MaximumSamples || body.Length != 12L + (fixedSize == 0 ? count * 4L : 0)) throw new InvalidDataException();
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
        { values[i] = fixedSize == 0 ? U32(body, 12 + i * 4) : fixedSize; if (values[i] == 0) throw new InvalidDataException(); }
        return values;
    }
    private static long[] Runs(ReadOnlyMemory<byte> body, int samples, bool composition)
    {
        byte version = Version(body);
        if (!composition && version != 0) throw new Unsupported();
        uint count = U32(body, 4);
        if (count > MaximumSamples || body.Length != 8L + count * 8L) throw new InvalidDataException();
        var result = new long[samples]; int at = 0;
        for (int i = 0; i < count; i++)
        {
            uint n = U32(body, 8 + i * 8), raw = U32(body, 12 + i * 8);
            long value = composition && version == 1 ? unchecked((int)raw) : raw;
            if (n == 0 || n > samples - at || !composition && value <= 0) throw new InvalidDataException();
            Array.Fill(result, value, at, (int)n); at += (int)n;
        }
        if (at != samples) throw new InvalidDataException();
        return result;
    }
    private static MediaOperationResult<Mp4NativeSampleEvidence> Fail(MediaFailureKind kind) =>
        MediaOperationResult<Mp4NativeSampleEvidence>.Failed(new(kind, "Progressive MP4 sample table을 지원 범위에서 확인하지 못했습니다.", "ProgressiveSamples"));
}
