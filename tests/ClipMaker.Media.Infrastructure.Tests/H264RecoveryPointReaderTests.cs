using System.Buffers.Binary;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class H264RecoveryPointReaderTests
{
    private static byte[] Packet(params byte[][] nals) => nals.SelectMany(nal =>
    {
        byte[] framed = new byte[4 + nal.Length]; BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)nal.Length);
        nal.CopyTo(framed, 4); return framed;
    }).ToArray();
    private static byte[] Recovery => [6, 6, 1, 0xc4, 0x80];
    private static byte[] Intra => [0x41, 0x88, 0x80];

    [Theory]
    [InlineData("success")]
    [InlineData("missing")]
    [InlineData("count")]
    [InlineData("idr")]
    [InlineData("inter")]
    [InlineData("header")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("truncated")]
    public void OnlyExactZeroRecoveryAndCompleteNonIdrIntraHeadersProduceEvidence(string fault)
    {
        byte[] recovery = Recovery, intra = Intra;
        if (fault == "count") recovery[3] = 0x54;
        if (fault == "idr") intra[0] = 0x65;
        if (fault == "inter") intra[1] = 0xe0;
        if (fault == "header") intra = [0x41];
        byte[] bytes = fault == "missing" ? Packet(intra) : fault == "duplicate" ? Packet(recovery, recovery, intra) :
            fault == "extra" ? Packet(recovery, intra, [9, 0x10]) : Packet(recovery, intra);
        if (fault == "truncated") bytes = bytes[..^1];
        var proof = new H264RecoveryPointReader().Read(bytes, 42);
        if (fault != "success") { Assert.Null(proof); return; }
        Assert.NotNull(proof); Assert.Equal(42, proof.Position); Assert.Equal(bytes.Length, proof.Size);
        Assert.Equal(0, proof.RecoveryFrameCount); Assert.True(proof.ExactMatch); Assert.False(proof.BrokenLink);
        Assert.Equal(0, proof.ChangingSliceGroupIdc); Assert.True(proof.AllSlicesIntra);
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("missing", false)]
    [InlineData("position", false)]
    [InlineData("size", false)]
    [InlineData("count", false)]
    [InlineData("intra", false)]
    [InlineData("hash", false)]
    [InlineData("audio", false)]
    public void ExtraCodecKeyRequiresBoundedRecoveryEvidenceWhileContainerNonsyncIsPreserved(string fault, bool expected)
    {
        var proof = new MediaH264RecoveryPointObservation(fault == "position" ? 43 : 42, fault == "size" ? 15 : 16,
            fault == "hash" ? "invalid" : new string('a', 64), fault == "count" ? 1 : 0, true, false, 0, fault != "intra");
        var packet = new MediaPacketObservation(0, 0, 0, 1, "K__", null, false, 16, 42, fault == "missing" ? null : proof);
        Assert.Equal(expected, Mp4NativeObservationVerifier.MatchesPacketSync(
            fault == "audio" ? MediaStreamKind.Audio : MediaStreamKind.Video, 0x10000, packet));
        Assert.False(Mp4NativeObservationVerifier.MatchesPacketSync(MediaStreamKind.Video, 0, packet with { Flags = "___" }));
    }

    [Fact]
    public async Task ByteReaderEnrichesOnlyMatchingNativePacketAndRetainsRawFlags()
    {
        byte[] bytes = Packet(Recovery, Intra);
        SignedMediaTime.TryCreate(1, 1, out var clock, out _);
        var raw = new MediaProbeObservations([new(0, MediaStreamKind.Video, clock!, 0, null, null, 1,
            new("h264", null, "yuv420p", null, null, null, 1))], [new(0, 0, 1, null, null, null, null)],
            [new(0, 0, 0, 1, "K__", null, false, bytes.Length, 0)], true, true);
        var native = new Mp4NativeSampleEvidence([new(1, "h264", 1, new string('a', 64), null, null)],
            [new(1, 0, 0, 1, (uint)bytes.Length, 0x10000, 0)]);
        var container = new Mp4FileTimingEvidence(new(1, [new(1, 1, false, [])]), [],
            MediaOperationResult<Mp4NativeSampleEvidence>.Succeeded(native));
        using var file = new MemoryStream(bytes, false);
        var observed = await new H264RecoveryPointReader().EnrichAsync(file, raw, container, TestContext.Current.CancellationToken);
        Assert.Null(raw.Packets[0].RecoveryPoint); Assert.NotNull(observed.Packets[0].RecoveryPoint);
        Assert.Equal(raw.Packets[0].Flags, observed.Packets[0].Flags); Assert.Equal(0x10000u, native.Samples[0].Flags);
        Assert.Equal(MediaOperationStatus.Succeeded, new Mp4NativeObservationVerifier().Verify(observed, container).Status);
        Assert.Equal("NativeSyncState", new Mp4NativeObservationVerifier().Verify(raw, container).Failure!.Field);
    }
}
