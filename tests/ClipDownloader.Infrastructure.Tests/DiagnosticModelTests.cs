using ClipDownloader.Application.Models;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class DiagnosticModelTests
{
    [Fact]
    public void OperationResultKeepsSuccessFailureAndCancellationMutuallyExclusive()
    {
        var success = OperationResult<string>.Succeeded("value");
        Assert.Equal(OperationStatus.Succeeded, success.Status);
        Assert.Equal("value", success.Data);
        Assert.Null(success.Error);
        var failed = OperationResult<string>.Failed(new(OperationErrorCode.ProcessFailed, OperationErrorCategory.Execution, "safe"));
        Assert.Equal(OperationStatus.Failed, failed.Status);
        Assert.Null(failed.Data);
        Assert.NotNull(failed.Error);
        var cancelled = OperationResult<string>.Cancelled();
        Assert.Equal(OperationStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Data);
        Assert.Null(cancelled.Error);
        Assert.Throws<ArgumentNullException>(() => OperationResult<string>.Succeeded(null!));
        Assert.Throws<ArgumentNullException>(() => OperationResult<string>.Failed(null!));
    }

    [Fact]
    public void RequestAndReportCollectionsSnapshotInputsAndRejectIncompleteReports()
    {
        var features = new List<ToolFeatureRequirement> { new(ToolFeatureKind.Encoder, "aac") };
        var request = new ToolDiagnosticRequest(ToolDiagnosticCommand.Clip, features);
        features.Clear();
        Assert.Single(request.RequiredFeatures);
        var inspections = new List<ToolDiagnosticInspection>();
        var location = new ToolDiagnostic("yt-dlp.exe", ToolDiagnosticStatus.Found, false, "path", ToolDiagnosticSource.Path, inspections, null);
        var tool = new ToolExecutionDiagnostic(ToolKind.YtDlp, location, ToolCheckStatus.Passed, ToolCheckStatus.Passed,
            "2026.08.19", 0, null, []);
        var tools = new List<ToolExecutionDiagnostic> { tool };
        var report = new ToolExecutionReport(ToolDiagnosticCommand.Info, tools);
        inspections.Add(new("changed", ToolDiagnosticSource.Path, ToolDiagnosticStatus.Missing, null));
        tools.Clear();
        Assert.Single(report.Tools);
        Assert.Empty(report.Tools[0].Location.Inspections);
        Assert.True(report.AllRequestedChecksPassed);
        Assert.Throws<ArgumentException>(() => new ToolExecutionReport(ToolDiagnosticCommand.Diagnose, []));
        Assert.Throws<ArgumentException>(() => new ToolExecutionReport(ToolDiagnosticCommand.Diagnose, [tool, tool, tool]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("aac;exec")]
    [InlineData("name with spaces")]
    [InlineData("한글")]
    public void FeatureIdentifiersCannotBeAmbiguousOrCommands(string name) =>
        Assert.Throws<ArgumentException>(() => new ToolFeatureRequirement(ToolFeatureKind.Encoder, name));

    [Fact]
    public void InfoAndDiagnoseCannotRequestMediaFeatureProbes()
    {
        Assert.Throws<ArgumentException>(() => new ToolDiagnosticRequest(ToolDiagnosticCommand.Info, [new(ToolFeatureKind.Encoder, "aac")]));
        Assert.Throws<ArgumentException>(() => new ToolDiagnosticRequest(ToolDiagnosticCommand.Diagnose, [new(ToolFeatureKind.Muxer, "mp4")]));
    }
}
