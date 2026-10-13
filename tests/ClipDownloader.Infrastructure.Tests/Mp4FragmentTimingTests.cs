using System.Buffers.Binary;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class Mp4FragmentTimingTests
{
    private static byte[] Box(string type, params byte[][] bodies)
    {
        byte[] body = bodies.SelectMany(b => b).ToArray(); var bytes = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4); body.CopyTo(bytes, 8); return bytes;
    }
    private static byte[] Header(string type, uint value)
    {
        var body = new byte[type switch { "mvhd" => 100, "tkhd" => 84, _ => 24 }];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), value); return Box(type, body);
    }
    private static byte[] Init() => Box("moov", Header("mvhd", 1000), Box("trak", Header("tkhd", 7), Box("mdia", Header("mdhd", 6000))));
    private static byte[] Tfhd(uint id = 7, byte version = 0)
    {
        var bytes = new byte[8]; bytes[0] = version; bytes[1] = 2;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), id); return Box("tfhd", bytes);
    }
    private static byte[] Tfdt(byte version = 0, ulong time = 12000)
    {
        var bytes = new byte[version == 0 ? 8 : 12]; bytes[0] = version;
        if (version == 0) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)time);
        else BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(4), time);
        return Box("tfdt", bytes);
    }
    private static async Task<OperationResult<Mp4FileTimingObservation>> Read(params byte[][] parts)
    {
        using var stream = new MemoryStream(parts.SelectMany(p => p).ToArray());
        return await new Mp4TimingReader().ReadAsync(stream, TestContext.Current.CancellationToken);
    }
    [Theory]
    [InlineData((byte)0, (ulong)uint.MaxValue)]
    [InlineData((byte)1, ulong.MaxValue)]
    public async Task UnsignedDecodeTimeIsPreservedWithTrackAndOffset(byte version, ulong time)
    {
        var init = Init(); var result = await Read(init, Box("moof", Box("traf", Tfhd(), Tfdt(version, time))));
        var anchor = Assert.Single(result.Data!.FragmentAnchors);
        Assert.Equal(7u, anchor.TrackId); Assert.Equal(time, anchor.BaseDecodeTime);
        Assert.Equal(init.Length, anchor.MoofOffset); Assert.Equal(0x020000u, anchor.HeaderFlags);
        Assert.True(((System.Collections.IList)result.Data.FragmentAnchors).IsReadOnly);
    }
    [Fact]
    public async Task MissingTfdtStaysUnknownAndNonfragmentedInputHasNoAnchors()
    {
        var result = await Read(Init(), Box("moof", Box("traf", Tfhd())));
        Assert.Null(Assert.Single(result.Data!.FragmentAnchors).BaseDecodeTime);
        Assert.Empty((await Read(Init())).Data!.FragmentAnchors);
    }
    [Fact]
    public async Task RepeatedTrackAcrossMoofsIsPreservedWithoutContinuityAssumption()
    {
        var result = await Read(Init(), Box("moof", Box("traf", Tfhd(), Tfdt())),
            Box("moof", Box("traf", Tfhd(), Tfdt(time: 0))));
        Assert.Equal(2, result.Data!.FragmentAnchors.Count);
        Assert.Equal(0ul, result.Data.FragmentAnchors[1].BaseDecodeTime);
        Assert.True(result.Data.FragmentAnchors[1].MoofOffset > result.Data.FragmentAnchors[0].MoofOffset);
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate_track")]
    [InlineData("duplicate_tfdt")]
    [InlineData("missing_tfhd")]
    [InlineData("short_tfdt")]
    [InlineData("empty_moof")]
    public async Task MalformedFragmentFailsWithoutObservation(string fault)
    {
        byte[] body = fault switch
        {
            "foreign" => Box("traf", Tfhd(9), Tfdt()),
            "duplicate_track" => Box("traf", Tfhd(), Tfdt()).Concat(Box("traf", Tfhd(), Tfdt())).ToArray(),
            "duplicate_tfdt" => Box("traf", Tfhd(), Tfdt(), Tfdt()),
            "missing_tfhd" => Box("traf", Tfdt()),
            "short_tfdt" => Box("traf", Tfhd(), Box("tfdt", new byte[4])),
            _ => []
        };
        var result = await Read(Init(), Box("moof", body));
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code); Assert.Null(result.Data);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownVersionsAreUnsupported(bool header)
    {
        var result = await Read(Init(), Box("moof", Box("traf", Tfhd(version: header ? (byte)1 : (byte)0), Tfdt(header ? (byte)0 : (byte)2))));
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code); Assert.Null(result.Data);
    }
    [Fact]
    public async Task PerMoofAndMoofCountLimitsAreEnforced()
    {
        Assert.Equal(OperationErrorCode.UnsupportedFormat,
            (await Read(Init(), Box("moof", new byte[Mp4TimingReader.MaximumMoofBytes + 1]))).Error!.Code);
        byte[] moof = Box("moof", Box("traf", Tfhd()));
        Assert.Equal(OperationErrorCode.UnsupportedFormat,
            (await Read(Init(), Enumerable.Repeat(moof, 1025).SelectMany(p => p).ToArray())).Error!.Code);
    }
    [Fact]
    public async Task TotalMoofBytesAreBounded()
    {
        byte[] body = Box("traf", Tfhd()).Concat(Box("free", new byte[2 * 1024 * 1024 - 40])).ToArray();
        byte[] moof = Box("moof", body);
        var result = await Read(Init(), Enumerable.Repeat(moof, 5).SelectMany(p => p).ToArray());
        Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code); Assert.Null(result.Data);
    }
}
