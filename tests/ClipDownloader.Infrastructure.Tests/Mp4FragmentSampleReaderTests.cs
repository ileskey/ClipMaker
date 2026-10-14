using ClipMaker.Media.Core.Models;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

internal static class NativeMp4Fixture
{
    internal static byte[] Box(string type, params byte[][] parts)
    {
        byte[] body = parts.SelectMany(p => p).ToArray(); byte[] bytes = new byte[body.Length + 8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length); Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4); body.CopyTo(bytes, 8); return bytes;
    }
    internal static byte[] Words(params uint[] words)
    { byte[] bytes = new byte[words.Length * 4]; for(int i=0;i<words.Length;i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i*4),words[i]); return bytes; }
    internal static byte[] Moov()
    {
        var tkhd = new byte[84]; BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), 7);
        byte[] entry = Box("avc1", new byte[78], Box("avcC", [1,100,0,31,255,225,0,2,103,100,1,0,2,104,0]));
        return Box("trak", Box("tkhd",tkhd), Box("mdia", Box("minf", Box("stbl", Box("stsd", Words(0,1), entry),
            Box("stts",Words(0,0)),Box("stsc",Words(0,0)),Box("stco",Words(0,0)),Box("stsz",Words(0,0,0))))))
            .Concat(Box("mvex", Box("trex", Words(0,7,1,100,4,0)))).ToArray();
    }
    internal static byte[] Moof(byte runVersion=0, uint offset=0, uint samples=2, uint headerFlags=0x020000)
    {
        uint runFlags=0x801;
        byte[] trun = Box("trun", Words(((uint)runVersion << 24)|runFlags,samples,108,offset,offset));
        return Box("traf", Box("tfhd",Words(headerFlags,7)), Box("tfdt",Words(0,12000)), trun);
    }
    internal static OperationResult<Mp4NativeSampleEvidence> Read(byte[]? moov=null, byte[]? moof=null,
        IReadOnlyList<Mp4PayloadRange>? payloads=null) => new Mp4FragmentSampleReader().Read(moov??Moov(),
            [new(1000,moof??Moof())],payloads??[new(1108,1116)]);
}

public sealed class Mp4FragmentSampleReaderTests
{
    private static byte[] AudioMoov(byte[] config)
    {
        byte[] Descriptor(byte tag, byte[] data) => new byte[] { tag, (byte)data.Length }.Concat(data).ToArray();
        var decoder = new byte[13]; decoder[0] = 0x40; decoder[1] = 0x15;
        var es = new byte[] { 0, 1, 0 }.Concat(Descriptor(4, decoder.Concat(Descriptor(5, config)).ToArray())).ToArray();
        var entry = new byte[28]; BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(24), 44100u << 16);
        var tkhd = new byte[84]; BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), 7);
        return NativeMp4Fixture.Box("trak", NativeMp4Fixture.Box("tkhd", tkhd), NativeMp4Fixture.Box("mdia",
            NativeMp4Fixture.Box("minf", NativeMp4Fixture.Box("stbl",
                NativeMp4Fixture.Box("stsd", NativeMp4Fixture.Words(0, 1), NativeMp4Fixture.Box("mp4a", entry,
                    NativeMp4Fixture.Box("esds", NativeMp4Fixture.Words(0), Descriptor(3, es)))),
                NativeMp4Fixture.Box("stts", NativeMp4Fixture.Words(0, 0)), NativeMp4Fixture.Box("stsc", NativeMp4Fixture.Words(0, 0)),
                NativeMp4Fixture.Box("stco", NativeMp4Fixture.Words(0, 0)), NativeMp4Fixture.Box("stsz", NativeMp4Fixture.Words(0, 0, 0))))))
            .Concat(NativeMp4Fixture.Box("mvex", NativeMp4Fixture.Box("trex", NativeMp4Fixture.Words(0, 7, 1, 1024, 4, 0)))).ToArray();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(16, false)]
    [InlineData(5, true)]
    [InlineData(17, false)]
    public void LcPaddingPreservesFullConfigurationAndRejectsUninterpretedExtensions(int size, bool nonzero)
    {
        byte[] config = new byte[size]; config[0] = 0x12; config[1] = 0x10;
        if (nonzero) config[^1] = 1;
        var result = NativeMp4Fixture.Read(moov: AudioMoov(config));
        if (nonzero || size > 16) Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        else
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            var actual = Assert.Single(result.Data!.Configurations);
            Assert.Equal(size, actual.ConfigBytes);
            Assert.Equal(44100, actual.SampleRate);
            Assert.Equal(2, actual.Channels);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(config)), actual.ConfigSha256);
        }
    }
    [Fact]
    public void DefaultsCompositionAndPhysicalBytesRemainExact()
    {
        var result=NativeMp4Fixture.Read(); Assert.Equal(OperationStatus.Succeeded,result.Status);
        var samples=result.Data!.Samples; Assert.Equal(2,samples.Count);
        Assert.Equal(new BigInteger(12000),samples[0].PresentationTicks); Assert.Equal(new BigInteger(12100),samples[1].DecodeTicks);
        Assert.Equal(1108,samples[0].ByteOffset); Assert.Equal(1112,samples[1].ByteOffset);
        Assert.Equal(100u,samples[0].Duration); Assert.Equal(4u,samples[0].Size);
        Assert.Equal("h264",Assert.Single(result.Data.Configurations).Codec);
        Assert.True(((System.Collections.IList)samples).IsReadOnly);
    }
    [Theory]
    [InlineData((byte)0,4294967295L)]
    [InlineData((byte)1,-1L)]
    public void CompositionOffsetVersionControlsSignedness(byte version,long offset)
    {
        var result=NativeMp4Fixture.Read(moof:NativeMp4Fixture.Moof(version,uint.MaxValue));
        Assert.Equal(new BigInteger(12000)+offset,result.Data!.Samples[0].PresentationTicks);
    }
    [Theory]
    [InlineData(1u)]
    [InlineData(0u)]
    [InlineData(0x030000u)]
    public void UnsupportedBaseOffsetFormsNeverBecomeNativeEvidence(uint flags) =>
        Assert.Equal(OperationErrorCode.UnsupportedFormat,NativeMp4Fixture.Read(moof:NativeMp4Fixture.Moof(headerFlags:flags)).Error!.Code);
    [Fact]
    public void MissingOrShortTimingAndMdatBoundsFail()
    {
        Assert.Equal(OperationErrorCode.InvalidMetadata,NativeMp4Fixture.Read(payloads:[new(1108,1112)]).Error!.Code);
        Assert.Null(NativeMp4Fixture.Read(moof:NativeMp4Fixture.Box("traf",NativeMp4Fixture.Box("tfhd",NativeMp4Fixture.Words(0x020000,7)))).Data);
        Assert.Null(NativeMp4Fixture.Read(moof:NativeMp4Fixture.Moof(samples:3)).Data);
    }
    [Fact]
    public void SampleCountLimitStopsBeforeAllocationLoop()
    {
        Assert.Equal(OperationErrorCode.UnsupportedFormat,NativeMp4Fixture.Read(moof:
            NativeMp4Fixture.Moof(samples:Mp4FragmentSampleReader.MaximumSamples+1)).Error!.Code);
    }
    [Fact]
    public void NoFragmentsAndAbsentSampleDescriptionDoNotPretendToBeVerified()
    {
        var result=new Mp4FragmentSampleReader().Read(NativeMp4Fixture.Moov(),[],[]);
        Assert.Equal(OperationErrorCode.UnsupportedFormat,result.Error!.Code);
        Assert.Null(NativeMp4Fixture.Read(moov:[]).Data);
    }
    [Fact]
    public void ReusedPhysicalBytesAcrossFragmentsAreRejected()
    {
        var result=new Mp4FragmentSampleReader().Read(NativeMp4Fixture.Moov(),
            [new(1000,NativeMp4Fixture.Moof()),new(1000,NativeMp4Fixture.Moof())],[new(1108,1116)]);
        Assert.Equal(OperationErrorCode.InvalidMetadata,result.Error!.Code);
    }
    [Fact]
    public void HybridStaticSampleTableCannotBeHiddenByFragmentEvidence()
    {
        var moov=NativeMp4Fixture.Moov();
        byte[] tag=Encoding.ASCII.GetBytes("stts");
        int position=Enumerable.Range(0,moov.Length-4).Single(index=>moov.AsSpan(index,4).SequenceEqual(tag));
        BinaryPrimitives.WriteUInt32BigEndian(moov.AsSpan(position+8),1);
        Assert.Equal(OperationErrorCode.UnsupportedFormat,NativeMp4Fixture.Read(moov:moov).Error!.Code);
    }
}
