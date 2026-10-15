using System.Buffers.Binary;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;
using F=ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class Mp4SidxReaderTests
{
    internal static byte[] Prefix(byte version=0,uint size=100,uint scale=6000,uint duration=6000,ulong earliest=12000,ulong firstOffset=8)
    {
        byte[] body=new byte[version==0?36:44];body[0]=version;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4),7);BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(8),scale);
        int at=12;
        if(version==0){BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(at),(uint)earliest);BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(at+4),(uint)firstOffset);at+=8;}
        else{BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(at),earliest);BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(at+8),firstOffset);at+=16;}
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(at+2),1);at+=4;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(at),size);BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(at+4),duration);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(at+8),0x90000000);
        return NativeMp4Fixture.Box("ftyp",[1,2,3,4]).Concat(NativeMp4Fixture.Box("moov")).Concat(NativeMp4Fixture.Box("sidx",body)).ToArray();
    }
    [Fact]
    public void IndexCoordinateAndPhysicalRangeAreSeparateExactObservations()
    {
        var prefix=Prefix();var result=new Mp4SidxReader().Read(prefix,1000);
        Assert.Equal(OperationStatus.Succeeded,result.Status);var data=result.Data!;
        Assert.Equal(7u,data.ReferenceId);Assert.Equal(6000u,data.Timescale);
        Assert.Equal(20,data.Initialization.Length);
        var reference=Assert.Single(data.References);Assert.Equal(prefix.Length+8,reference.Start);Assert.Equal(100,reference.Bytes);
        Assert.Equal(F.Time(2),reference.IndexRange.Start);Assert.Equal(F.Time(3),reference.IndexRange.End);
        Assert.True(reference.StartsWithSap);Assert.Equal(1,reference.SapType);
        Assert.True(((System.Collections.IList)data.References).IsReadOnly);
    }
    [Fact]
    public void VersionOneUnsignedTimestampDoesNotWrapToSignedLong()
    {
        var result=new Mp4SidxReader().Read(Prefix(version:1,earliest:ulong.MaxValue),1000);
        Assert.Equal(System.Numerics.BigInteger.Parse(ulong.MaxValue.ToString()),result.Data!.References[0].IndexRange.Start.Numerator*6000/result.Data.References[0].IndexRange.Start.Denominator);
    }
    [Theory]
    [InlineData("hierarchical")]
    [InlineData("version")]
    public void UnsupportedIndexFormsNeverBecomeDirectReferences(string kind)
    {
        var result=new Mp4SidxReader().Read(kind=="hierarchical"?Prefix(size:0x80000064):Prefix(version:2),1000);
        Assert.Equal(OperationErrorCode.UnsupportedFormat,result.Error!.Code);Assert.Null(result.Data);
    }
    [Theory]
    [InlineData("scale")]
    [InlineData("size")]
    [InlineData("duration")]
    [InlineData("offset")]
    [InlineData("resource")]
    public void InvalidBoundsAndScalarsAreRejected(string kind)
    {
        var prefix=kind switch{"scale"=>Prefix(scale:0),"size"=>Prefix(size:0),"duration"=>Prefix(duration:0),"offset"=>Prefix(version:1,firstOffset:ulong.MaxValue),_=>Prefix()};
        var result=new Mp4SidxReader().Read(prefix,kind=="resource"?prefix.Length:1000);
        Assert.Equal(OperationErrorCode.InvalidMetadata,result.Error!.Code);Assert.Null(result.Data);
    }
    [Fact]
    public void PrefixMayEndInsideMediaBoxWithoutCopyingPayload()
    {
        var prefix=Prefix();var mdat=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(mdat,100000);System.Text.Encoding.ASCII.GetBytes("mdat").CopyTo(mdat,4);
        var result=new Mp4SidxReader().Read(prefix.Concat(mdat).ToArray(),1000000);
        Assert.Equal(OperationStatus.Succeeded,result.Status);Assert.Equal(20,result.Data!.Initialization.Length);
    }
    [Fact]
    public void DuplicateIndexAndMissingInitAreRejected()
    {
        var prefix=Prefix();Assert.Null(new Mp4SidxReader().Read(prefix.Concat(prefix[20..]).ToArray(),1000).Data);
        Assert.Null(new Mp4SidxReader().Read(prefix[20..],1000).Data);
        Assert.Null(new Mp4SidxReader().Read(new byte[Mp4SidxReader.MaximumPrefixBytes+1],long.MaxValue).Data);
    }
}
