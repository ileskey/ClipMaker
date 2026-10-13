using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsNativeReferenceVerifierTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("double")]
    [InlineData("empty")]
    [InlineData("rate")]
    [InlineData("clipped")]
    public void IndexedMovieTranslationIsAppliedExactlyOnceAndUnsupportedEditsFail(string kind)
    {
        var original = Reference();
        var local = new IndexedCollectedReference(original.Reference.File, 1116, 1116, new string('A', 64), F.Range(0, 2), F.Range(0, 2));
        long shift = kind == "double" ? -200 : -100;
        var stream = original.Observations.Streams[0] with { StartPts = 12000 + shift };
        var observations = new InitialReferenceObservations([stream],
            original.Observations.Frames.Select(f => f with { Pts = f.Pts + shift }),
            original.Observations.Packets.Select(p => p with { Pts = p.Pts + shift, Dts = p.Dts + shift }), true);
        var edit = new Mp4EditObservation(kind == "clipped" ? 1u : 18000u, kind == "empty" ? -1 : 100,
            kind == "rate" ? (short)2 : (short)1, 0);
        var track = new Mp4TrackTiming(7, 6000, true, [edit]);
        var reference = original with { Reference = local, Observations = observations,
            ContainerTiming = new(6000, [track]), TrackLinks = [new(0, track)] };
        var result = new HlsNativeReferenceVerifier().Verify(reference);
        if (kind == "valid")
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(F.Time(11900, 6000), result.Data!.EarliestPresentation);
        }
        else
        {
            Assert.Null(result.Data);
            Assert.Equal(kind == "double" ? OperationErrorCode.VerificationFailed : OperationErrorCode.UnsupportedFormat, result.Error!.Code);
        }
    }
    internal static ProbedInitialReference Reference(OwnedMediaFile? file=null)
    {
        var native = NativeMp4Fixture.Read();
        var config = Assert.Single(native.Data!.Configurations);
        var stream = new InitialStreamObservation(0,MediaStreamKind.Video,F.Time(1,6000),12000,F.Rate(60),F.Rate(60),7,
            new("h264","High","yuv420p",null,null,null,config.ConfigBytes));
        var packets = native.Data.Samples.Select(sample => new InitialPacketObservation(0,(long)sample.PresentationTicks,
            (long)sample.DecodeTicks,sample.Duration,"K__",null,false,(int)sample.Size,sample.ByteOffset)).ToArray();
        var frames = native.Data.Samples.Select(sample => new InitialFrameObservation(0,(long)sample.PresentationTicks,
            sample.Duration,null,null,null,null)).ToArray();
        var collected = new HlsCollectedReference(Guid.NewGuid(),file??new(Guid.NewGuid(),Path.GetFullPath(".artifacts/native-reference.mp4"),["v"]),
            1116,1116,new string('A',64),F.Range(0,2),F.Range(0,2));
        var timing = new Mp4TrackTiming(7,6000,false,Array.AsReadOnly(Array.Empty<Mp4EditObservation>()));
        return new(collected,"fixture",new([stream],frames,packets,true),new(6000,[timing]),[new(0,timing)],[],native);
    }
    [Fact]
    public void ActualSampleBytesAndDecoderTimingAreRequiredForNativeIdentity()
    {
        var reference=Reference(); var result=new HlsNativeReferenceVerifier().Verify(reference);
        Assert.Equal(OperationStatus.Succeeded,result.Status); Assert.Equal(F.Time(2),result.Data!.EarliestPresentation);
        Assert.Equal(F.Time(12200,6000),Assert.Single(result.Data.Streams).ObservedRange.End);
        Assert.Same(reference,result.Data.Reference);
    }
    [Theory]
    [InlineData("pts")]
    [InlineData("dts")]
    [InlineData("bytes")]
    [InlineData("size")]
    [InlineData("duration")]
    [InlineData("corrupt")]
    [InlineData("discard")]
    [InlineData("side")]
    [InlineData("sync")]
    public void PacketContradictionsNeverYieldVerifiedReference(string fault)
    {
        var r=Reference(); var packets=r.Observations.Packets.ToArray(); var p=packets[0];
        packets[0]=fault switch {
            "pts"=>p with{Pts=null},"dts"=>p with{Dts=0},"bytes"=>p with{Position=0},"size"=>p with{Size=5},
            "duration"=>p with{Duration=99},"corrupt"=>p with{Flags="K_C"},"discard"=>p with{Flags="KD_"},
            "sync"=>p with{Flags="___"},
            _=>p with{HasUninterpretedSideData=true}};
        var result=new HlsNativeReferenceVerifier().Verify(r with{Observations=new(r.Observations.Streams,r.Observations.Frames,packets,true)});
        Assert.Equal(OperationErrorCode.VerificationFailed,result.Error!.Code); Assert.Null(result.Data);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingFramesOrPtsRemainUnverified(bool missing)
    {
        var r=Reference(); var frames=r.Observations.Frames.ToArray();
        if(missing) frames=frames[..1]; else frames[0]=frames[0] with{Pts=null};
        var result=new HlsNativeReferenceVerifier().Verify(r with{Observations=new(r.Observations.Streams,frames,r.Observations.Packets,true)});
        Assert.Equal(OperationErrorCode.VerificationFailed,result.Error!.Code); Assert.Null(result.Data);
    }
    [Fact]
    public void EditsAndNativeEvidenceAbsenceAreDifferentFailures()
    {
        var r=Reference();
        Assert.Equal(OperationErrorCode.VerificationFailed,new HlsNativeReferenceVerifier().Verify(r with{NativeSamples=null}).Error!.Code);
        var edited=r.ContainerTiming.Tracks[0] with{HasEditList=true};
        Assert.Equal(OperationErrorCode.UnsupportedFormat,new HlsNativeReferenceVerifier().Verify(r with{ContainerTiming=new(6000,[edited])}).Error!.Code);
    }
    [Fact]
    public void DecoderProfileSizeAndStartChangesCannotBeIgnored()
    {
        var r=Reference(); var stream=r.Observations.Streams[0];
        foreach(var changed in new[]{stream with{StartPts=0},stream with{TimeBase=F.Time(1,3000)},stream with{Codec=stream.Codec with{ExtradataSize=1}}})
            Assert.Null(new HlsNativeReferenceVerifier().Verify(r with{Observations=new([changed],r.Observations.Frames,r.Observations.Packets,true)}).Data);
    }
}
