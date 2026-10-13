using System.Buffers.Binary;
using System.Security.Cryptography;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class PackedAacReferenceReaderTests
{
    private static byte[] Segment(ulong stamp = 1234, byte version = 3, bool duplicate = false)
    {
        byte[] body = "com.apple.streaming.transportStreamTimestamp\0"u8.ToArray().Concat(new byte[8]).ToArray();
        BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(body.Length - 8), stamp);
        byte[] frame = new byte[10 + body.Length]; "PRIV"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)body.Length); body.CopyTo(frame, 10);
        byte[] tag = duplicate ? frame.Concat(frame).ToArray() : frame;
        byte[] header = new byte[10]; "ID3"u8.CopyTo(header); header[3] = version;
        header[8] = (byte)(tag.Length >> 7); header[9] = (byte)(tag.Length & 127);
        byte[] aac = [255, 241, 80, 128, 1, 63, 252, 1, 2];
        return header.Concat(tag).Concat(aac).Concat(aac).ToArray();
    }
    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    public void CarrierClockAndExactPayloadRemainSeparate(byte version)
    {
        var result = new PackedAacReferenceReader().Read(Segment(version: version));
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal(1234ul, result.Data!.TransportTicks);
        Assert.Equal(2, result.Data.Frames.Count);
        Assert.All(result.Data.Frames, f =>
        {
            Assert.Equal(44100, f.SampleRate); Assert.Equal(2, f.Channels); Assert.Equal(1024, f.Samples);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2 })), f.PayloadSha256);
        });
        Assert.True(((System.Collections.IList)result.Data.Frames).IsReadOnly);
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("overflow")]
    [InlineData("truncated")]
    [InlineData("synchsafe")]
    [InlineData("adts")]
    [InlineData("missing")]
    public void MalformedCarrierAndFrameBoundsCannotPublishFacts(string fault)
    {
        var bytes = Segment(stamp: fault == "overflow" ? 0x200000000ul : 1234, duplicate: fault == "duplicate");
        if (fault == "truncated") bytes = bytes[..^1];
        if (fault == "synchsafe") bytes[6] = 128;
        if (fault == "adts") bytes[^9] = 0;
        if (fault == "missing") bytes[20] = (byte)'X';
        var result = new PackedAacReferenceReader().Read(bytes);
        Assert.Null(result.Data); Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code);
    }
    [Theory]
    [InlineData("version")]
    [InlineData("tagflags")]
    [InlineData("frameflags")]
    [InlineData("profile")]
    [InlineData("blocks")]
    public void UnsupportedLayoutsAreExplicit(string fault)
    {
        var bytes = Segment();
        switch (fault)
        {
            case "version": bytes[3] = 2; break;
            case "tagflags": bytes[5] = 128; break;
            case "frameflags": bytes[18] = 1; break;
            case "profile": bytes[^7] |= 128; break;
            case "blocks": bytes[^3] |= 1; break;
        }
        var result = new PackedAacReferenceReader().Read(bytes);
        Assert.Null(result.Data); Assert.Equal(OperationErrorCode.UnsupportedFormat, result.Error!.Code);
    }
}
