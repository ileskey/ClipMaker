using System.Buffers.Binary;
using System.Text;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Infrastructure.Probing;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class LocalMp4ProgressiveSampleReaderTests
{
    private static byte[] Box(string type, params byte[][] bodies)
    {
        var data = bodies.SelectMany(b => b).ToArray(); var bytes = new byte[data.Length + 8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length); Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        data.CopyTo(bytes, 8); return bytes;
    }
    private static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }
    [Theory]
    [InlineData("success")]
    [InlineData("run-count")]
    [InlineData("zero-size")]
    [InlineData("payload")]
    [InlineData("overlap")]
    [InlineData("sync-duplicate")]
    public void TablesRetainSignedCompositionAndCheckedPhysicalSamplesOrRejectInconsistentBounds(string fault)
    {
        var header = new byte[84]; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 7);
        var entry = new byte[78];
        var description = Box("stsd", Words(0, 1), Box("avc1", entry, Box("avcC", new byte[] { 1, 100, 0, 40, 255, 0xe1, 0 })));
        var sizes = Box("stsz", Words(0, 0, 2, fault == "zero-size" ? 0u : 6u, 6));
        var durations = Box("stts", Words(0, 1, fault == "run-count" ? 3u : 2u, 100));
        var composition = Box("ctts", Words(0x01000000, 2, 1, unchecked((uint)-20), 1, 20));
        var offsets = Box("stco", Words(0, 2, 100, fault == "overlap" ? 103u : 106u));
        var chunks = Box("stsc", Words(0, 1, 1, 1, 1));
        var sync = fault == "sync-duplicate" ? Box("stss", Words(0, 2, 1, 1)) : Box("stss", Words(0, 1, 1));
        var moov = Box("trak", Box("tkhd", header), Box("mdia", Box("minf", Box("stbl", description, sizes, durations, composition, offsets, chunks, sync))));
        var result = new LocalMp4ProgressiveSampleReader().Read(moov, [new(100, fault == "payload" ? 111 : 112)]);
        if (fault == "success")
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
            var samples = result.Data!.Samples;
            Assert.Equal(2, samples.Count); Assert.Equal(-20, samples[0].PresentationTicks);
            Assert.Equal(120, samples[1].PresentationTicks); Assert.Equal(100, samples[1].DecodeTicks);
            Assert.Equal(106, samples[1].ByteOffset); Assert.Equal(0u, samples[0].Flags);
            Assert.Equal(0x01010000u, samples[1].Flags);
        }
        else { Assert.Equal(MediaFailureKind.InvalidMedia, result.Failure!.Kind); Assert.Null(result.Data); }
    }
}
