using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.Models;
using Xunit;
using F=ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class SelectedSourceAcquisitionServiceTests
{
    internal sealed class Fixture:IDisposable
    {
        internal string DirectoryPath {get;}=Path.Combine(Path.GetFullPath(".artifacts"),"source-service-tests",Guid.NewGuid().ToString("N"));
        internal SelectedSourceInspectionRequest Request {get;}
        internal SelectedSourceLocators Locators {get;}
        internal OwnedMediaFile File {get;}
        internal Fixture(bool missingRate=false,long bytes=4096,long span=4,SourceInspectionPurpose? purpose=null)
        {
            Directory.CreateDirectory(DirectoryPath);
            VideoSourceReference.TryCreate(VideoPlatform.Chzzk,"123",out var source);
            var format=new VideoFormat("v",1920,1080,missingRate?null:F.Rate(60),"h264",null,StreamPresence.Present,StreamPresence.Absent,Transport:FormatTransport.Hls);
            var info=new VideoInfo(source!,null,TimeSpan.FromSeconds(120),null,null,null,VideoState.Completed,[format],[]);
            var choice=FormatSelection.Create(info,QualityOption.Best,format,null,FormatSelectionReason.RequestedBest).Data!;
            var snapshot=SelectedSourceSnapshot.Create(Guid.NewGuid(),info,choice,ClipMode.Fast).Data!;
            Request=new(Guid.NewGuid(),snapshot,purpose??(SourceInspectionPurpose.FrameRate|SourceInspectionPurpose.SourceTimeline),DirectoryPath,new(bytes,F.Time(span)));
            Locators=new(snapshot,info,new(format,new("https://media.example/leaf.m3u8"),null,new Dictionary<string,string>()),null);
            File=new(Guid.NewGuid(),Path.Combine(DirectoryPath,"initial.mp4"),["v"]);
            System.IO.File.WriteAllBytes(File.Path,new byte[1116]);
        }
        internal SelectedSourceAcquisitionService Service(Func<ProbedInitialReference,ProbedInitialReference>? change=null) =>new(
            (_,_)=>Task.FromResult(OperationResult<SelectedSourceLocators>.Succeeded(Locators)),
            (_,_,_)=>Task.FromResult(new HlsInitialReferenceInspectionResult(OperationStatus.Succeeded,
                change?.Invoke(HlsNativeReferenceVerifierTests.Reference(File))??HlsNativeReferenceVerifierTests.Reference(File),null)));
        public void Dispose(){foreach(var file in Directory.GetFiles(DirectoryPath))System.IO.File.Delete(file);Directory.Delete(DirectoryPath);}
    }
    [Fact]
    public async Task VerifiedInitialFactsRemainScopedAndEndStaysUnknown()
    {
        using var f=new Fixture();using var service=f.Service();
        var result=await service.InspectAsync(f.Request,TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded,result.Status);var data=result.Data!;
        Assert.Equal(F.Rate(60),data.FrameRate.Value);Assert.Equal(F.Time(2),data.Timeline.Origin.Value);
        Assert.Equal(MediaInspectionState.Verified,data.Timeline.SharedAxis.State);
        Assert.Equal(StreamPresence.Absent,data.Timeline.AudioPresence);Assert.Equal(MediaInspectionState.Unknown,data.Timeline.NativeEnd.State);
        Assert.Equal(data.InspectionId,data.Timeline.Origin.Scope.SnapshotId);Assert.Equal(MediaTimeBasis.Native,data.Timeline.Origin.Scope.TimeBasis);
        var resources=service.GetOwnedResources(f.Request.JobId);Assert.Single(resources.Files);Assert.Null(resources.Warning);
        Assert.True(((System.Collections.IList)resources.Files).IsReadOnly);
    }
    [Fact]
    public async Task MissingMetadataRateCanUseExactBoundedObservationWithoutChangingSelection()
    {
        using var f=new Fixture(missingRate:true);using var service=f.Service();
        var result=await service.InspectAsync(f.Request,TestContext.Current.CancellationToken);
        Assert.Equal(F.Rate(60),result.Data!.FrameRate.Value);Assert.Null(f.Request.Selection.Selection.VideoFormat.FrameRate);
    }
    [Fact]
    public async Task ConflictingKnownRateFailsAndRetainsOwnedResources()
    {
        using var f=new Fixture();using var service=f.Service(r=>r with{Observations=new(
            [r.Observations.Streams[0] with{DeclaredRate=F.Rate(30)}],r.Observations.Frames,r.Observations.Packets,true)});
        var result=await service.InspectAsync(f.Request,TestContext.Current.CancellationToken);
        Assert.Equal("SelectedFrameRateChanged",result.Error!.Field);Assert.Null(result.Data);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error.Code);
        Assert.Equal(OperationErrorCategory.Execution, result.Error.Category);
        Assert.Single(service.GetOwnedResources(f.Request.JobId).Files);Assert.NotNull(service.GetOwnedResources(f.Request.JobId).Warning);
    }
    [Theory]
    [InlineData(OperationStatus.Failed)]
    [InlineData(OperationStatus.Cancelled)]
    public async Task PublicFailureDataIsNullButOwnedFileNeverDisappears(OperationStatus status)
    {
        using var f=new Fixture();using var service=new SelectedSourceAcquisitionService(
            (_,_)=>Task.FromResult(OperationResult<SelectedSourceLocators>.Succeeded(f.Locators)),
            (_,_,_)=>Task.FromResult(new HlsInitialReferenceInspectionResult(status,null,status==OperationStatus.Failed?
                new(OperationErrorCode.ProcessTerminationFailed,OperationErrorCategory.Execution,"종료 미확인"):null,[f.File])));
        var result=await service.InspectAsync(f.Request,TestContext.Current.CancellationToken);
        Assert.Equal(status,result.Status);Assert.Null(result.Data);
        var resources=service.GetOwnedResources(f.Request.JobId);Assert.Single(resources.Files);Assert.NotNull(resources.Warning);
        Assert.Equal(status==OperationStatus.Failed,resources.HasUnconfirmedProcess);
        if(status==OperationStatus.Failed)Assert.Equal(OperationErrorCode.ProcessTerminationFailed,(await service.InspectAsync(f.Request,TestContext.Current.CancellationToken)).Error!.Code);
    }
    [Fact]
    public async Task NativeFailureDoesNotPublishOriginAndStillTracksFile()
    {
        using var f=new Fixture();using var service=f.Service(r=>r with{NativeSamples=null});
        var result=await service.InspectAsync(f.Request,TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed,result.Error!.Code);Assert.Null(result.Data);
        Assert.Single(service.GetOwnedResources(f.Request.JobId).Files);
    }
    [Fact]
    public async Task PreCancellationDoesNotResolveOrInspect()
    {
        using var f=new Fixture();using var service=new SelectedSourceAcquisitionService((_,_)=>throw new InvalidOperationException(),(_,_,_)=>throw new InvalidOperationException());
        Assert.Equal(OperationStatus.Cancelled,(await service.InspectAsync(f.Request,new(true))).Status);
        Assert.Empty(service.GetOwnedResources(f.Request.JobId).Files);
    }
    [Fact]
    public async Task SameJobCannotBeReboundToAnotherSelection()
    {
        using var f=new Fixture();using var service=f.Service();
        Assert.Equal(OperationStatus.Succeeded,(await service.InspectAsync(f.Request,TestContext.Current.CancellationToken)).Status);
        var other=F.Snapshot(Guid.NewGuid(),rate:F.Rate(60));
        var request=new SelectedSourceInspectionRequest(f.Request.JobId,other,f.Request.Purpose,f.DirectoryPath,f.Request.Budget);
        Assert.Equal("JobBinding",(await service.InspectAsync(request,TestContext.Current.CancellationToken)).Error!.Field);
        Assert.Single(service.GetOwnedResources(f.Request.JobId).Files);
    }
    [Fact]
    public async Task CancellationAfterOwnedCandidateLeavesNullDataAndQueryableResources()
    {
        using var f=new Fixture();using var cancellation=new CancellationTokenSource();
        using var service=new SelectedSourceAcquisitionService((_,_)=>Task.FromResult(OperationResult<SelectedSourceLocators>.Succeeded(f.Locators)),
            (_,_,_)=>{cancellation.Cancel();return Task.FromResult(new HlsInitialReferenceInspectionResult(OperationStatus.Succeeded,HlsNativeReferenceVerifierTests.Reference(f.File),null));});
        Assert.Equal(OperationStatus.Cancelled,(await service.InspectAsync(f.Request,cancellation.Token)).Status);
        Assert.True(service.GetOwnedResources(f.Request.JobId).NeedsCleanup);Assert.Single(service.GetOwnedResources(f.Request.JobId).Files);
    }
}
