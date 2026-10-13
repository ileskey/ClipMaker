using System.Net;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;
using F=ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;
using Fixture=ClipDownloader.Infrastructure.Tests.SelectedSourceAcquisitionServiceTests.Fixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class HlsAcquireServiceTests
{
    private const string Playlist="#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:2,\na.m4s\n#EXTINF:2,\nb.m4s\n#EXT-X-ENDLIST\n";
    private sealed class Handler:HttpMessageHandler
    {
        internal int Calls{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])});}
    }
    private sealed class Locator:IToolLocator
    {public ToolLocationResult Locate(ToolSearchRequest request,CancellationToken token)=>new(ToolLocationStatus.Missing,false,null,null,[],null);}
    private sealed class Runner:IProcessRunner
    {public Task<ProcessResult> RunAsync(ProcessRequest request,Action<ProcessOutputChunk>? progress,CancellationToken token)=>throw new InvalidOperationException();}
    private static async Task<(SelectedSourceAcquisitionService Service,AcquisitionRequest Request,Handler Handler)> Prepare(
        Fixture fixture,string fault)
    {
        var handler=new Handler();var http=new BoundedSourceHttpReader(handler);int inspected=0;
        var service=new SelectedSourceAcquisitionService((_,_)=>Task.FromResult(OperationResult<SelectedSourceLocators>.Succeeded(fixture.Locators)),
            (request,locators,token)=>
            {
                inspected++;
                if(inspected>1 && fault=="cancel") return Task.FromResult(new HlsInitialReferenceInspectionResult(OperationStatus.Cancelled,null,null,[fixture.File]));
                if(inspected>1 && fault=="termination") return Task.FromResult(new HlsInitialReferenceInspectionResult(OperationStatus.Failed,null,
                    new(OperationErrorCode.ProcessTerminationFailed,OperationErrorCategory.Execution,"종료 미확인"),[fixture.File]));
                var reference=HlsNativeReferenceVerifierTests.Reference(fixture.File);
                if(inspected>1 && fault=="changed") reference=reference with{Reference=new HlsCollectedReference(((HlsCollectedReference)reference.Reference).PlaylistId,fixture.File,1116,1116,new string('B',64),F.Range(0,2),F.Range(0,2))};
                var playlist=new HlsMediaPlaylistParser().Parse(Encoding.UTF8.GetBytes(Playlist),locators.Video.Uri).Data!;
                return Task.FromResult(new HlsInitialReferenceInspectionResult(OperationStatus.Succeeded,reference,null,playlist:playlist));
            },http,new(new Locator(),new(),new Runner()));
        var inspectedResult=await service.InspectAsync(fixture.Request,TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded,inspectedResult.Status);
        return(service,new(fixture.Request.JobId,fixture.Request.Selection,inspectedResult.Data!,F.UserRange(F.Rate(60)),fixture.DirectoryPath),handler);
    }
    [Theory]
    [InlineData("changed","SourceReferenceChanged")]
    [InlineData("budget","SourceByteBudget")]
    [InlineData("span",null)]
    public async Task KnownChangeAndBudgetsStopBeforeTargetHttp(string fault,string? field)
    {
        using var f=new Fixture(bytes:fault=="budget"?1116:4096,span:fault=="span"?1:4);
        var prepared=await Prepare(f,fault);using var service=prepared.Service;
        var result=await service.AcquireAsync(prepared.Request,null,TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Failed,result.Status);Assert.Null(result.Data);
        if(field is not null) Assert.Equal(field,result.Error!.Field);
        Assert.Equal(0,prepared.Handler.Calls);Assert.True(service.GetOwnedResources(f.Request.JobId).NeedsCleanup);
    }
    [Theory]
    [InlineData("cancel",OperationStatus.Cancelled)]
    [InlineData("termination",OperationStatus.Failed)]
    public async Task RefreshCancellationAndTerminationKeepRegistry(string fault,OperationStatus status)
    {
        using var f=new Fixture();var prepared=await Prepare(f,fault);using var service=prepared.Service;
        var result=await service.AcquireAsync(prepared.Request,null,TestContext.Current.CancellationToken);
        Assert.Equal(status,result.Status);Assert.Null(result.Data);
        var resources=service.GetOwnedResources(f.Request.JobId);Assert.Single(resources.Files);Assert.NotNull(resources.Warning);
        Assert.Equal(fault=="termination",resources.HasUnconfirmedProcess);Assert.Equal(0,prepared.Handler.Calls);
    }
    [Fact]
    public async Task PreCancellationDoesNotStartTargetTransfer()
    {
        using var f=new Fixture();var prepared=await Prepare(f,"");using var service=prepared.Service;
        Assert.Equal(OperationStatus.Cancelled,(await service.AcquireAsync(prepared.Request,null,new(true))).Status);
        Assert.Equal(0,prepared.Handler.Calls);Assert.Single(service.GetOwnedResources(f.Request.JobId).Files);
    }
    [Fact]
    public async Task UnrequestedTimelineDoesNotBecomeImplicitlyVerified()
    {
        using var f=new Fixture(purpose:SourceInspectionPurpose.FrameRate);var prepared=await Prepare(f,"");using var service=prepared.Service;
        Assert.Equal("SourceTimelineUnverified",(await service.AcquireAsync(prepared.Request,null,TestContext.Current.CancellationToken)).Error!.Field);
        Assert.Equal(0,prepared.Handler.Calls);
    }
    [Fact]
    public async Task InspectionCannotBeBorrowedAcrossServiceInstances()
    {
        using var f=new Fixture();var prepared=await Prepare(f,"");using var original=prepared.Service;using var other=f.Service();
        Assert.Equal("InspectionBinding",(await other.AcquireAsync(prepared.Request,null,TestContext.Current.CancellationToken)).Error!.Field);
        Assert.Empty(other.GetOwnedResources(f.Request.JobId).Files);
    }
}
