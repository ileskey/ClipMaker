using System.Buffers.Binary;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class Mp4TimingReaderTests
{
    private static byte[] Box(string type, params byte[][] bodies)
    {
        byte[] data = bodies.SelectMany(b => b).ToArray(); var bytes = new byte[data.Length + 8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4); data.CopyTo(bytes, 8); return bytes;
    }
    private static byte[] Header(string type, uint value, byte version = 0)
    {
        int length = type switch { "mvhd" => version == 0 ? 100 : 112, "tkhd" => version == 0 ? 84 : 96, _ => version == 0 ? 24 : 36 };
        var bytes = new byte[length]; bytes[0] = version;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(version == 0 ? 12 : 20), value); return Box(type, bytes);
    }
    private static byte[] Edit(byte version = 0, ulong duration = 100, long time = -1, short rate = 1, short fraction = 0)
    {
        int width = version == 0 ? 12 : 20; var bytes = new byte[8 + width]; bytes[0] = version;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 1);
        if (version == 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)duration);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(12), (int)time);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), duration);
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(16), time);
        }
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(bytes.Length - 4), rate);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(bytes.Length - 2), fraction);
        return Box("elst", bytes);
    }
    private static byte[] Track(uint id = 7, byte version = 0, byte[]? edits = null) => Box("trak",
        Header("tkhd", id, version), Box("mdia", Header("mdhd", 48000, version)), edits ?? []);
    private static byte[] Moov(params byte[][] tracks) => Header("mvhd", 1000).Concat(tracks.SelectMany(t => t)).ToArray();

    [Fact]
    public void V0EmptyEditAndTrackClockRemainSeparate()
    {
        var result = new Mp4TimingReader().ParseMoov(Moov(Track(edits: Box("edts", Edit()))));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(1000u, result.Data!.MovieTimescale);
        var track = Assert.Single(result.Data.Tracks); Assert.Equal(7u, track.TrackId); Assert.Equal(48000u, track.MediaTimescale);
        Assert.True(track.HasEditList); Assert.Equal(-1, Assert.Single(track.Edits).MediaTime);
        Assert.Equal(100ul, track.Edits[0].MovieDuration);
    }
    [Fact]
    public void V1UnsignedDurationSignedTimeAndRatesAreNotFlattened()
    {
        var result = new Mp4TimingReader().ParseMoov(Moov(Track(version: 1,
            edits: Box("edts", Edit(1, ulong.MaxValue, long.MinValue, 0, 3)))));
        var edit = Assert.Single(result.Data!.Tracks[0].Edits);
        Assert.Equal(ulong.MaxValue, edit.MovieDuration); Assert.Equal(long.MinValue, edit.MediaTime);
        Assert.Equal(0, edit.RateInteger); Assert.Equal(3, edit.RateFraction);
    }
    [Fact]
    public void AbsentAndEmptyEditListRemainDistinct()
    {
        byte[] empty = new byte[8];
        var result = new Mp4TimingReader().ParseMoov(Moov(Track(), Track(9, edits: Box("edts", Box("elst", empty))))).Data!;
        Assert.False(result.Tracks[0].HasEditList); Assert.True(result.Tracks[1].HasEditList);
        Assert.Empty(result.Tracks[1].Edits);
        Assert.True(((System.Collections.IList)result.Tracks).IsReadOnly);
        Assert.True(((System.Collections.IList)result.Tracks[1].Edits).IsReadOnly);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MissingDuplicateZeroAndTruncatedHeadersFail(int fault)
    {
        byte[] input = fault switch
        {
            0 => Track(),
            1 => Moov(Track(), Track()),
            2 => Moov(Track(0)),
            3 => Header("mvhd", 0).Concat(Track()).ToArray(),
            _ => Moov(Track()).Concat(new byte[] { 1 }).ToArray()
        };
        var result = new Mp4TimingReader().ParseMoov(input);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code); Assert.Null(result.Data);
    }
    [Fact]
    public void UnknownVersionAndMoovCapAreUnsupported()
    {
        var header = Header("mvhd", 1000); header[8] = 2;
        Assert.Equal(OperationErrorCode.UnsupportedFormat, new Mp4TimingReader().ParseMoov(header.Concat(Track()).ToArray()).Error!.Code);
        Assert.Equal(OperationErrorCode.UnsupportedFormat, new Mp4TimingReader().ParseMoov(new byte[Mp4TimingReader.MaximumMoovBytes + 1]).Error!.Code);
    }
    [Fact]
    public void V1MovieClockAndZeroSizeLastChildAreRead()
    {
        var track = Track(); BinaryPrimitives.WriteUInt32BigEndian(track, 0);
        var result = new Mp4TimingReader().ParseMoov(Header("mvhd", 90000, 1).Concat(track).ToArray());
        Assert.Equal(90000u, result.Data!.MovieTimescale);
        Assert.Single(result.Data.Tracks);
    }
    [Fact]
    public void EditCountAndEntryBoundsAreChecked()
    {
        var edit = Edit(); BinaryPrimitives.WriteUInt32BigEndian(edit.AsSpan(12), 17);
        Assert.Equal(OperationErrorCode.UnsupportedFormat,
            new Mp4TimingReader().ParseMoov(Moov(Track(edits: Box("edts", edit)))).Error!.Code);
        BinaryPrimitives.WriteUInt32BigEndian(edit.AsSpan(12), 2);
        Assert.Equal(OperationErrorCode.InvalidMetadata,
            new Mp4TimingReader().ParseMoov(Moov(Track(edits: Box("edts", edit)))).Error!.Code);
    }
    [Fact]
    public void MissingDuplicateAndShortRequiredBoxesFail()
    {
        foreach (var track in new[] {
            Box("trak", Header("tkhd", 1)),
            Box("trak", Header("tkhd", 1), Box("mdia", Header("mdhd", 48000), Header("mdhd", 48000))),
            Box("trak", Box("tkhd", new byte[16]), Box("mdia", Header("mdhd", 48000))),
            Track(edits: Box("edts", Edit(), Edit())) })
            Assert.Equal(OperationErrorCode.InvalidMetadata, new Mp4TimingReader().ParseMoov(Moov(track)).Error!.Code);
    }
    [Fact]
    public void BoxCountIsBounded()
    {
        byte[] excess = Enumerable.Repeat(Box("free"), 10001).SelectMany(b => b).ToArray();
        Assert.Equal(OperationErrorCode.InvalidMetadata, new Mp4TimingReader().ParseMoov(excess).Error!.Code);
    }
    [Fact]
    public async Task ExtendedMoovSizeAndSkippedMediaPayloadAreHandled()
    {
        byte[] payload = Moov(Track()); var extended = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(extended, 1); Encoding.ASCII.GetBytes("moov").CopyTo(extended, 4);
        BinaryPrimitives.WriteUInt64BigEndian(extended.AsSpan(8), (ulong)extended.Length); payload.CopyTo(extended, 16);
        using var stream = new CountingStream(Box("mdat", new byte[4 * 1024 * 1024]).Concat(extended).ToArray());
        var result = await new Mp4TimingReader().ReadAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status); Assert.True(stream.ReadBytes < 1024);
    }
    [Fact]
    public async Task DuplicateMoovMissingMoovAndInvalidBoundsFail()
    {
        var moov = Box("moov", Moov(Track()));
        foreach (var bytes in new[] { moov.Concat(moov).ToArray(), Box("mdat", [1]), new byte[] { 0, 0, 0, 20, 109, 111, 111, 118 } })
        {
            using var stream = new MemoryStream(bytes);
            Assert.Equal(OperationErrorCode.InvalidMetadata, (await new Mp4TimingReader().ReadAsync(stream, TestContext.Current.CancellationToken)).Error!.Code);
        }
    }
    [Fact]
    public async Task CancellationDoesNotReturnObservation()
    {
        using var stream = new MemoryStream(Box("moov", Moov(Track())));
        Assert.Equal(OperationStatus.Cancelled, (await new Mp4TimingReader().ReadAsync(stream, new(true))).Status);
        Assert.Equal(0, stream.Position);
    }
    private sealed class CountingStream(byte[] data) : MemoryStream(data)
    {
        internal int ReadBytes { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { int count = await base.ReadAsync(buffer, token); ReadBytes += count; return count; }
    }
}
