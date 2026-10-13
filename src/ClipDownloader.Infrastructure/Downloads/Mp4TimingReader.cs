using System.Buffers.Binary;
using System.Text;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record Mp4EditObservation(ulong MovieDuration, long MediaTime, short RateInteger, short RateFraction);
internal sealed record Mp4TrackTiming(uint TrackId, uint MediaTimescale, bool HasEditList,
    IReadOnlyList<Mp4EditObservation> Edits);
internal sealed record Mp4TimingObservation(uint MovieTimescale, IReadOnlyList<Mp4TrackTiming> Tracks);
internal sealed record Mp4FragmentDecodeAnchor(long MoofOffset, uint TrackId, uint HeaderFlags, ulong? BaseDecodeTime);
internal sealed record Mp4FileTimingObservation(Mp4TimingObservation Initialization,
    IReadOnlyList<Mp4FragmentDecodeAnchor> FragmentAnchors, OperationResult<Mp4NativeSampleEvidence> NativeSamples);
internal sealed record Mp4MoofBody(long Offset, ReadOnlyMemory<byte> Body);

// Container header observations only. Track IDs are not FFprobe stream indices.
internal sealed class Mp4TimingReader
{
    internal const int MaximumMoovBytes = 2 * 1024 * 1024;
    internal const int MaximumMoofBytes = 2 * 1024 * 1024;
    internal const int MaximumTotalMoofBytes = 8 * 1024 * 1024;
    private const int MaximumBoxes = 10_000;
    internal sealed record Box(string Type, ReadOnlyMemory<byte> Body);
    internal sealed class Unsupported : Exception { }

    internal async Task<OperationResult<Mp4FileTimingObservation>> ReadAsync(Stream input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("A readable seekable local stream is required.", nameof(input));
        try
        {
            token.ThrowIfCancellationRequested();
            input.Position = 0;
            long end = input.Length;
            byte[] header = new byte[16];
            byte[]? moov = null;
            var anchors = new List<Mp4FragmentDecodeAnchor>();
            var fragments = new List<Mp4MoofBody>();
            var payloads = new List<Mp4PayloadRange>();
            int moofBytes = 0, moofCount = 0, fragmentBoxes = 0;
            int count = 0;
            while (input.Position < end)
            {
                token.ThrowIfCancellationRequested();
                if (++count > MaximumBoxes || end - input.Position < 8) throw new InvalidDataException();
                long start = input.Position;
                await input.ReadExactlyAsync(header.AsMemory(0, 8), token).ConfigureAwait(false);
                ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
                int headerBytes = 8;
                if (size == 1)
                {
                    if (end - input.Position < 8) throw new InvalidDataException();
                    await input.ReadExactlyAsync(header.AsMemory(8, 8), token).ConfigureAwait(false);
                    size = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); headerBytes = 16;
                }
                if (size == 0) size = (ulong)(end - start);
                if (size < (ulong)headerBytes || size > (ulong)(end - start)) throw new InvalidDataException();
                string type = Encoding.ASCII.GetString(header, 4, 4);
                if (type == "moov")
                {
                    if (moov is not null) throw new InvalidDataException();
                    if (size - (ulong)headerBytes > MaximumMoovBytes) throw new Unsupported();
                    moov = new byte[(int)size - headerBytes];
                    await input.ReadExactlyAsync(moov, token).ConfigureAwait(false);
                }
                else if (type == "moof")
                {
                    ulong bodyBytes = size - (ulong)headerBytes;
                    if (++moofCount > 1024 || bodyBytes > MaximumMoofBytes ||
                        bodyBytes > (ulong)(MaximumTotalMoofBytes - moofBytes)) throw new Unsupported();
                    moofBytes += (int)bodyBytes;
                    var body = new byte[(int)bodyBytes];
                    await input.ReadExactlyAsync(body, token).ConfigureAwait(false);
                    anchors.AddRange(FragmentAnchors(body, start, ref fragmentBoxes));
                    fragments.Add(new(start, body));
                }
                else if (type == "mdat") payloads.Add(new(start + headerBytes, checked(start + (long)size)));
                // Media payload is skipped, never allocated or decoded here.
                input.Position = checked(start + (long)size);
            }
            if (moov is null) throw new InvalidDataException();
            token.ThrowIfCancellationRequested();
            var result = ParseMoov(moov);
            token.ThrowIfCancellationRequested();
            if (result.Status != OperationStatus.Succeeded) return OperationResult<Mp4FileTimingObservation>.Failed(result.Error!);
            if (anchors.Any(anchor => !result.Data!.Tracks.Any(track => track.TrackId == anchor.TrackId)))
                throw new InvalidDataException();
            var samples = new Mp4FragmentSampleReader().Read(moov, fragments, payloads);
            token.ThrowIfCancellationRequested();
            return OperationResult<Mp4FileTimingObservation>.Succeeded(new(result.Data!, anchors.AsReadOnly(), samples));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return OperationResult<Mp4FileTimingObservation>.Cancelled(); }
        catch (Unsupported) { return FileFailed(OperationErrorCode.UnsupportedFormat); }
        catch (Exception error) when (error is IOException or InvalidDataException or OverflowException or UnauthorizedAccessException)
        { return FileFailed(OperationErrorCode.InvalidMetadata); }
    }

    private static IEnumerable<Mp4FragmentDecodeAnchor> FragmentAnchors(ReadOnlyMemory<byte> body, long offset, ref int count)
    {
        var result = new List<Mp4FragmentDecodeAnchor>();
        var ids = new HashSet<uint>();
        foreach (var traf in Boxes(body, ref count).Where(box => box.Type == "traf"))
        {
            if (result.Count == 16) throw new Unsupported();
            var children = Boxes(traf.Body, ref count);
            var tfhd = Single(children, "tfhd").Body;
            if (Version(tfhd) != 0) throw new Unsupported();
            uint id = U32(tfhd, 4);
            if (id == 0 || !ids.Add(id)) throw new InvalidDataException();
            uint flags = U32(tfhd, 0) & 0x00ffffff;
            var tfdt = children.Where(box => box.Type == "tfdt").ToArray();
            if (tfdt.Length > 1) throw new InvalidDataException();
            ulong? decodeTime = null;
            if (tfdt.Length == 1)
            {
                var value = tfdt[0].Body;
                byte version = Version(value);
                if ((U32(value, 0) & 0x00ffffff) != 0) throw new Unsupported();
                if (value.Length != (version == 0 ? 8 : 12)) throw new InvalidDataException();
                decodeTime = version == 0 ? U32(value, 4) : BinaryPrimitives.ReadUInt64BigEndian(value.Span[4..]);
            }
            // Only the tfhd prefix and tfdt value are interpreted. Optional tfhd fields/trun are not validated here.
            result.Add(new(offset, id, flags, decodeTime));
        }
        if (result.Count == 0) throw new InvalidDataException();
        return result;
    }

    internal OperationResult<Mp4TimingObservation> ParseMoov(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumMoovBytes) return Failed(OperationErrorCode.UnsupportedFormat);
        try
        {
            int count = 0;
            var children = Boxes(bytes, ref count);
            uint scale = Timescale(Single(children, "mvhd").Body, 100, 112);
            var tracks = new List<Mp4TrackTiming>();
            foreach (var track in children.Where(box => box.Type == "trak"))
            {
                if (tracks.Count == 16) throw new Unsupported();
                var trackBoxes = Boxes(track.Body, ref count);
                var header = Single(trackBoxes, "tkhd").Body;
                int offset = Version(header) == 0 ? 12 : 20;
                if (header.Length < (offset == 12 ? 84 : 96)) throw new InvalidDataException();
                uint id = U32(header, offset);
                if (id == 0 || tracks.Any(t => t.TrackId == id)) throw new InvalidDataException();
                var media = Boxes(Single(trackBoxes, "mdia").Body, ref count);
                uint mediaScale = Timescale(Single(media, "mdhd").Body, 24, 36);
                var editContainers = trackBoxes.Where(b => b.Type == "edts").ToArray();
                if (editContainers.Length > 1) throw new InvalidDataException();
                bool hasEdits = editContainers.Length == 1;
                var edits = hasEdits ? Edits(Single(Boxes(editContainers[0].Body, ref count), "elst").Body) : [];
                tracks.Add(new(id, mediaScale, hasEdits, Array.AsReadOnly(edits)));
            }
            if (tracks.Count == 0) throw new InvalidDataException();
            return OperationResult<Mp4TimingObservation>.Succeeded(new(scale, tracks.AsReadOnly()));
        }
        catch (Unsupported) { return Failed(OperationErrorCode.UnsupportedFormat); }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        { return Failed(OperationErrorCode.InvalidMetadata); }
    }

    internal static List<Box> Boxes(ReadOnlyMemory<byte> memory, ref int count)
    {
        var boxes = new List<Box>(); int position = 0;
        while (position < memory.Length)
        {
            if (++count > MaximumBoxes || memory.Length - position < 8) throw new InvalidDataException();
            var span = memory.Span[position..];
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(span); int header = 8;
            if (size == 1)
            {
                if (span.Length < 16) throw new InvalidDataException();
                size = BinaryPrimitives.ReadUInt64BigEndian(span[8..]); header = 16;
            }
            if (size == 0) size = (ulong)span.Length;
            if (size < (ulong)header || size > (ulong)span.Length) throw new InvalidDataException();
            boxes.Add(new(Encoding.ASCII.GetString(span.Slice(4, 4)), memory.Slice(position + header, (int)size - header)));
            position += (int)size;
        }
        return boxes;
    }

    internal static Box Single(List<Box> boxes, string type)
    {
        var selected = boxes.Where(box => box.Type == type).ToArray();
        return selected.Length == 1 ? selected[0] : throw new InvalidDataException();
    }
    internal static byte Version(ReadOnlyMemory<byte> memory)
    {
        if (memory.Length < 4) throw new InvalidDataException();
        if (memory.Span[0] > 1) throw new Unsupported();
        return memory.Span[0];
    }
    internal static uint U32(ReadOnlyMemory<byte> memory, int offset)
    {
        if (memory.Length - offset < 4) throw new InvalidDataException();
        return BinaryPrimitives.ReadUInt32BigEndian(memory.Span[offset..]);
    }
    private static uint Timescale(ReadOnlyMemory<byte> memory, int v0Minimum, int v1Minimum)
    {
        byte version = Version(memory);
        if (memory.Length < (version == 0 ? v0Minimum : v1Minimum)) throw new InvalidDataException();
        uint value = U32(memory, version == 0 ? 12 : 20);
        return value > 0 ? value : throw new InvalidDataException();
    }
    private static Mp4EditObservation[] Edits(ReadOnlyMemory<byte> memory)
    {
        byte version = Version(memory); uint count = U32(memory, 4);
        if (count > 16) throw new Unsupported();
        int width = version == 0 ? 12 : 20;
        if (memory.Length != 8 + count * width) throw new InvalidDataException();
        var edits = new Mp4EditObservation[count];
        for (int i = 0; i < edits.Length; i++)
        {
            var entry = memory.Span.Slice(8 + i * width, width);
            ulong duration = version == 0 ? BinaryPrimitives.ReadUInt32BigEndian(entry) : BinaryPrimitives.ReadUInt64BigEndian(entry);
            long time = version == 0 ? BinaryPrimitives.ReadInt32BigEndian(entry[4..]) : BinaryPrimitives.ReadInt64BigEndian(entry[8..]);
            // Preserve rate/empty-edit observations; supported native semantics are decided separately.
            edits[i] = new(duration, time, BinaryPrimitives.ReadInt16BigEndian(entry[^4..]), BinaryPrimitives.ReadInt16BigEndian(entry[^2..]));
        }
        return edits;
    }
    private static OperationResult<Mp4TimingObservation> Failed(OperationErrorCode code) => OperationResult<Mp4TimingObservation>.Failed(
        new(code, OperationErrorCategory.Execution, "MP4 timing header를 제한 범위에서 확인하지 못했습니다."));
    private static OperationResult<Mp4FileTimingObservation> FileFailed(OperationErrorCode code) => OperationResult<Mp4FileTimingObservation>.Failed(
        new(code, OperationErrorCategory.Execution, "MP4 초기/fragment timing header를 제한 범위에서 확인하지 못했습니다."));
}
