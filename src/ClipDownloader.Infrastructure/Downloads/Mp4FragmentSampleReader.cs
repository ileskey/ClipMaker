using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using static ClipDownloader.Infrastructure.Downloads.Mp4TimingReader;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record Mp4TrackConfiguration(uint TrackId, string Codec, int ConfigBytes, string ConfigSha256,
    int? SampleRate, int? Channels);
internal sealed record Mp4NativeSample(uint TrackId, BigInteger DecodeTicks, BigInteger PresentationTicks,
    uint Duration, uint Size, uint Flags, long ByteOffset);
internal sealed record Mp4PayloadRange(long Start, long End);
internal sealed class Mp4NativeSampleEvidence(IEnumerable<Mp4TrackConfiguration> configurations, IEnumerable<Mp4NativeSample> samples)
{
    internal IReadOnlyList<Mp4TrackConfiguration> Configurations { get; } = Array.AsReadOnly(configurations.ToArray());
    internal IReadOnlyList<Mp4NativeSample> Samples { get; } = Array.AsReadOnly(samples.ToArray());
}

// Narrow original-fragment timing reader. It does not qualify processing or infer missing timing.
internal sealed class Mp4FragmentSampleReader
{
    internal const int MaximumSamples = 100_000;
    private sealed record Defaults(uint Description, uint Duration, uint Size, uint Flags);

    internal OperationResult<Mp4NativeSampleEvidence> Read(ReadOnlyMemory<byte> moov, IReadOnlyList<Mp4MoofBody> fragments,
        IReadOnlyList<Mp4PayloadRange> payloads)
    {
        try
        {
            if (moov.Length > MaximumMoovBytes || fragments.Count is < 1 or > 1024 ||
                fragments.Sum(f => (long)f.Body.Length) > MaximumTotalMoofBytes) throw new Unsupported();
            int count = 0;
            var boxes = Boxes(moov, ref count);
            var configurations = new List<Mp4TrackConfiguration>();
            foreach (var track in boxes.Where(box => box.Type == "trak"))
            {
                if (configurations.Count == 16) throw new Unsupported();
                var children = Boxes(track.Body, ref count);
                var header = Single(children, "tkhd").Body;
                uint id = U32(header, Version(header) == 0 ? 12 : 20);
                if (id == 0 || configurations.Any(c => c.TrackId == id)) throw new InvalidDataException();
                var media = Boxes(Single(children, "mdia").Body, ref count);
                var minf = Boxes(Single(media, "minf").Body, ref count);
                var stbl = Boxes(Single(minf, "stbl").Body, ref count);
                // This strategy reads a fragmented bootstrap, not a hybrid progressive/fragmented movie.
                foreach (string type in new[] { "stts", "stsc", "stco" })
                {
                    var table = Single(stbl, type).Body;
                    if (Version(table) != 0 || U32(table, 4) != 0 || table.Length != 8) throw new Unsupported();
                }
                var sizes = Single(stbl, "stsz").Body;
                if (Version(sizes) != 0 || U32(sizes, 8) != 0 || sizes.Length != 12) throw new Unsupported();
                var stsd = Single(stbl, "stsd").Body;
                if (Version(stsd) != 0 || U32(stsd, 4) != 1) throw new Unsupported();
                var entries = Boxes(stsd[8..], ref count);
                if (entries.Count != 1) throw new InvalidDataException();
                configurations.Add(Configuration(id, entries[0], ref count));
            }
            var extensions = Boxes(Single(boxes, "mvex").Body, ref count);
            var defaults = new Dictionary<uint, Defaults>();
            foreach (var entry in extensions.Where(box => box.Type == "trex"))
            {
                if (Version(entry.Body) != 0 || entry.Body.Length != 24) throw new InvalidDataException();
                uint id = U32(entry.Body, 4);
                if (!configurations.Any(c => c.TrackId == id) || !defaults.TryAdd(id,
                    new(U32(entry.Body, 8), U32(entry.Body, 12), U32(entry.Body, 16), U32(entry.Body, 20)))) throw new InvalidDataException();
            }
            if (defaults.Count != configurations.Count) throw new InvalidDataException();
            var samples = new List<Mp4NativeSample>();
            foreach (var fragment in fragments)
            {
                if (fragment.Body.Length > MaximumMoofBytes) throw new Unsupported();
                foreach (var traf in Boxes(fragment.Body, ref count).Where(box => box.Type == "traf"))
                {
                    var children = Boxes(traf.Body, ref count);
                    var tfhd = Single(children, "tfhd").Body;
                    if (Version(tfhd) != 0) throw new Unsupported();
                    uint flags = U32(tfhd, 0) & 0xffffff;
                    if ((flags & 0x020000) == 0 || (flags & ~0x02003au) != 0) throw new Unsupported();
                    uint id = U32(tfhd, 4);
                    if (!defaults.TryGetValue(id, out var baseline)) throw new InvalidDataException();
                    int position = 8;
                    uint description = (flags & 2) != 0 ? Next(tfhd, ref position) : baseline.Description;
                    uint duration = (flags & 8) != 0 ? Next(tfhd, ref position) : baseline.Duration;
                    uint size = (flags & 16) != 0 ? Next(tfhd, ref position) : baseline.Size;
                    uint sampleFlags = (flags & 32) != 0 ? Next(tfhd, ref position) : baseline.Flags;
                    if (position != tfhd.Length) throw new InvalidDataException();
                    if (description != 1) throw new Unsupported();
                    var tfdt = Single(children, "tfdt").Body;
                    byte version = Version(tfdt);
                    if (tfdt.Length != (version == 0 ? 8 : 12) || (U32(tfdt, 0) & 0xffffff) != 0) throw new InvalidDataException();
                    BigInteger decode = version == 0 ? U32(tfdt, 4) : BinaryPrimitives.ReadUInt64BigEndian(tfdt.Span[4..]);
                    var runs = children.Where(box => box.Type == "trun").ToArray();
                    if (runs.Length == 0) throw new InvalidDataException();
                    long? dataPosition = null;
                    foreach (var run in runs)
                    {
                        byte runVersion = Version(run.Body);
                        uint runFlags = U32(run.Body, 0) & 0xffffff;
                        if ((runFlags & ~0x000f05u) != 0 || (runFlags & 0x404) == 0x404) throw new Unsupported();
                        uint entriesCount = U32(run.Body, 4);
                        if (entriesCount > MaximumSamples - samples.Count) throw new Unsupported();
                        position = 8;
                        if ((runFlags & 1) != 0) dataPosition = checked(fragment.Offset + unchecked((int)Next(run.Body, ref position)));
                        if (dataPosition is null) throw new Unsupported();
                        uint firstFlags = (runFlags & 4) != 0 ? Next(run.Body, ref position) : sampleFlags;
                        for (uint i = 0; i < entriesCount; i++)
                        {
                            uint d = (runFlags & 0x100) != 0 ? Next(run.Body, ref position) : duration;
                            uint s = (runFlags & 0x200) != 0 ? Next(run.Body, ref position) : size;
                            uint f = (runFlags & 0x400) != 0 ? Next(run.Body, ref position) : i == 0 ? firstFlags : sampleFlags;
                            uint rawOffset = (runFlags & 0x800) != 0 ? Next(run.Body, ref position) : 0;
                            BigInteger offset = runVersion == 0 ? rawOffset : unchecked((int)rawOffset);
                            if (d == 0 || s == 0) throw new InvalidDataException();
                            long dataEnd = checked(dataPosition.Value + s);
                            if (!payloads.Any(payload => dataPosition.Value >= payload.Start && dataEnd <= payload.End)) throw new InvalidDataException();
                            samples.Add(new(id, decode, decode + offset, d, s, f, dataPosition.Value));
                            dataPosition = dataEnd;
                            decode += d;
                        }
                        if (position != run.Body.Length) throw new InvalidDataException();
                    }
                }
            }
            if (samples.Count == 0) throw new InvalidDataException();
            var physical = samples.OrderBy(sample => sample.ByteOffset).ToArray();
            for (int i = 1; i < physical.Length; i++)
                if (physical[i - 1].ByteOffset + physical[i - 1].Size > physical[i].ByteOffset) throw new InvalidDataException();
            return OperationResult<Mp4NativeSampleEvidence>.Succeeded(new(configurations, samples));
        }
        catch (Unsupported) { return Fail(OperationErrorCode.UnsupportedFormat); }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        { return Fail(OperationErrorCode.InvalidMetadata); }
    }

    private static uint Next(ReadOnlyMemory<byte> bytes, ref int position)
    { uint value = U32(bytes, position); position += 4; return value; }

    private static Mp4TrackConfiguration Configuration(uint id, Box entry, ref int count)
    {
        ReadOnlyMemory<byte> config;
        if (entry.Type == "avc1")
        {
            if (entry.Body.Length < 78) throw new InvalidDataException();
            config = Single(Boxes(entry.Body[78..], ref count), "avcC").Body;
            if (config.Length < 7 || config.Span[0] != 1 || (config.Span[4] & 3) != 3) throw new Unsupported();
            return new(id, "h264", config.Length, Convert.ToHexString(SHA256.HashData(config.Span)), null, null);
        }
        if (entry.Type != "mp4a") throw new Unsupported();
        if (entry.Body.Length < 28) throw new InvalidDataException();
        if (BinaryPrimitives.ReadUInt16BigEndian(entry.Body.Span[8..]) != 0) throw new Unsupported();
        var esds = Single(Boxes(entry.Body[28..], ref count), "esds").Body;
        if (Version(esds) != 0) throw new Unsupported();
        config = AacConfiguration(esds[4..]);
        if (config.Length != 2) throw new Unsupported();
        ushort bits = BinaryPrimitives.ReadUInt16BigEndian(config.Span);
        int objectType = bits >> 11, frequencyIndex = (bits >> 7) & 15, channels = (bits >> 3) & 15;
        int[] rates = [96000,88200,64000,48000,44100,32000,24000,22050,16000,12000,11025,8000,7350];
        if (objectType != 2 || frequencyIndex >= rates.Length || channels is < 1 or > 2 || (bits & 7) != 0) throw new Unsupported();
        int rate = rates[frequencyIndex];
        if (rate > ushort.MaxValue) throw new Unsupported();
        if (BinaryPrimitives.ReadUInt16BigEndian(entry.Body.Span[16..]) != channels || U32(entry.Body, 24) != (uint)rate << 16)
            throw new InvalidDataException();
        return new(id, "aac", 2, Convert.ToHexString(SHA256.HashData(config.Span)), rate, channels);
    }

    private static ReadOnlyMemory<byte> AacConfiguration(ReadOnlyMemory<byte> bytes)
    {
        var es = Descriptor(bytes, 3);
        if (es.Length < 3) throw new InvalidDataException();
        int offset = 3; byte flags = es.Span[2];
        if ((flags & 0x80) != 0) offset += 2;
        if ((flags & 0x40) != 0) { if (offset >= es.Length) throw new InvalidDataException(); offset += 1 + es.Span[offset]; }
        if ((flags & 0x20) != 0) offset += 2;
        var decoder = Descriptor(es[offset..], 4);
        if (decoder.Length < 13 || decoder.Span[0] != 0x40 || (decoder.Span[1] >> 2) != 5) throw new Unsupported();
        return Descriptor(decoder[13..], 5);
    }
    private static ReadOnlyMemory<byte> Descriptor(ReadOnlyMemory<byte> memory, byte expected)
    {
        if (memory.Length < 2 || memory.Span[0] != expected) throw new InvalidDataException();
        int position = 1, length = 0;
        for (int i = 0; i < 4; i++)
        {
            if (position >= memory.Length) throw new InvalidDataException();
            byte value = memory.Span[position++]; length = checked((length << 7) | (value & 127));
            if ((value & 128) == 0)
            {
                if (length > memory.Length - position) throw new InvalidDataException();
                return memory.Slice(position, length);
            }
        }
        throw new InvalidDataException();
    }
    private static OperationResult<Mp4NativeSampleEvidence> Fail(OperationErrorCode code) => OperationResult<Mp4NativeSampleEvidence>.Failed(
        new(code, OperationErrorCategory.Execution, "원본 fragment sample/codec 설정을 지원 범위에서 확인하지 못했습니다.", "NativeSampleEvidence"));
}
