using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class InitialReferenceProbeTests
{
    private const string Json = """{"streams":[{"index":0,"codec_type":"video","time_base":"1/6000"}],"frames":[]}""";
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetFullPath(".artifacts"), "initial-probe-tests", Guid.NewGuid().ToString("N"));
        internal HlsCollectedReference Reference { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "ref.mp4");
            byte[] bytes = [1, 2, 3]; File.WriteAllBytes(path, bytes);
            SignedMediaTime.TryCreate(0, 1, out var zero, out _);
            SignedMediaTime.TryCreate(2, 1, out var two, out _);
            PresentationRange.TryCreate(zero!, two!, out var range, out _);
            Reference = new(Guid.NewGuid(), new(Guid.NewGuid(), path, ["selected"]), 3, 4,
                Convert.ToHexString(SHA256.HashData(bytes)), range!, range!);
        }
        public void Dispose() { File.Delete(Reference.File.Path); Directory.Delete(directory); }
    }
    private sealed class Locator(bool missing = false) : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken token) =>
            new(missing ? ToolLocationStatus.Missing : ToolLocationStatus.Found, false,
                missing ? null : Path.GetFullPath("ffprobe.exe"), ToolLocationSource.Path, [], null);
    }
    private sealed class Runner : IProcessRunner
    {
        internal List<ProcessRequest> Calls { get; } = [];
        internal Func<ProcessRequest, ProcessResult>? Respond { get; init; }
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? outputReceived, CancellationToken token)
        {
            Assert.Null(outputReceived); Calls.Add(request);
            return Task.FromResult(Respond?.Invoke(request) ?? Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1 full" : Json));
        }
    }
    private static ProcessResult Result(string output) => new(ProcessStatus.Exited, 0,
        new(Encoding.UTF8.GetBytes(output), Encoding.UTF8.GetByteCount(output), false, false, true, true),
        new(Array.Empty<byte>(), 0, false, false, true, true), null, true, true, true, null);
    private static InitialReferenceProbe Probe(Runner runner, bool missing = false) => new(new Locator(missing), new(), runner);

    [Fact]
    public async Task LocalProbeBindsIdentityAndUsesRestrictedArguments()
    {
        using var fixture = new Fixture(); var runner = new Runner();
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Same(fixture.Reference, result.Data!.Reference); Assert.Equal("8.1", result.Data.ProbeVersion);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.Calls[0].Timeout);
        var request = runner.Calls[1]; Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
        Assert.Equal(new[] { "-i", fixture.Reference.File.Path }, request.Arguments.TakeLast(2));
        Assert.Contains("file", request.Arguments); Assert.Contains("mov", request.Arguments);
        Assert.Equal(ProcessOutputMode.Full, request.StandardOutputMode);
        Assert.Equal(ProcessOutputMode.Tail, request.StandardErrorMode);
        Assert.DoesNotContain(request.Arguments, value => value.Contains("https://"));
        Assert.True(File.Exists(fixture.Reference.File.Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedBytesOrLengthStopsBeforeExecution(bool length)
    {
        using var fixture = new Fixture(); var runner = new Runner();
        File.WriteAllBytes(fixture.Reference.File.Path, length ? [1] : [9, 2, 3]);
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code); Assert.Null(result.Data); Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FileRemainsLockedDuringProbe()
    {
        using var fixture = new Fixture();
        var runner = new Runner { Respond = request =>
        {
            Assert.Throws<IOException>(() => File.WriteAllBytes(fixture.Reference.File.Path, [9]));
            return Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : Json);
        }};
        Assert.Equal(OperationStatus.Succeeded, (await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData(ProcessStatus.TimedOut, OperationErrorCode.TimedOut)]
    [InlineData(ProcessStatus.ExecutionFailed, OperationErrorCode.ProcessFailed)]
    [InlineData(ProcessStatus.TerminationFailed, OperationErrorCode.ProcessTerminationFailed)]
    public async Task FailedProcessNeverPublishesObservation(ProcessStatus status, OperationErrorCode expected)
    {
        using var fixture = new Fixture();
        var runner = new Runner { Respond = request => request.Arguments.Contains("-version") ? Result("ffprobe version 8.1") : Result("SECRET") with { Status = status } };
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Error!.Code); Assert.Null(result.Data); Assert.DoesNotContain("SECRET", result.Error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task IncompleteOutputAndUnconfirmedTerminationBlockSuccess(int fault)
    {
        using var fixture = new Fixture();
        var runner = new Runner { Respond = request => request.Arguments.Contains("-version") ? Result("ffprobe version 8.1") : fault switch
        {
            0 => Result(Json) with { StandardOutput = new(Array.Empty<byte>(), 0, false, true, true, true) },
            1 => Result(Json) with { StandardOutput = new(Array.Empty<byte>(), 0, true, false, false, false) },
            _ => Result(Json) with { DescendantExitConfirmed = false }
        }};
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Null(result.Data); Assert.Equal(fault == 2 ? OperationErrorCode.ProcessTerminationFailed : OperationErrorCode.InvalidMetadata, result.Error!.Code);
    }

    [Fact]
    public async Task MissingToolAndInvalidVersionDoNotRunProbe()
    {
        using var fixture = new Fixture(); var runner = new Runner();
        Assert.Equal(OperationErrorCode.MissingTool, (await Probe(runner, true).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Empty(runner.Calls);
        runner = new() { Respond = _ => Result("unknown") };
        Assert.Equal(OperationErrorCode.ProcessFailed, (await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task CancellationAfterResponseDoesNotPublishObservation()
    {
        using var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        var runner = new Runner { Respond = request =>
        {
            if (!request.Arguments.Contains("-version")) cancellation.Cancel();
            return Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : Json);
        }};
        Assert.Equal(OperationStatus.Cancelled, (await Probe(runner).ProbeAsync(fixture.Reference, cancellation.Token)).Status);
        Assert.Equal(OperationStatus.Cancelled, (await Probe(runner).ProbeAsync(fixture.Reference, new(true))).Status);
        Assert.Equal(2, runner.Calls.Count);
        Assert.True(File.Exists(fixture.Reference.File.Path));
    }
}
