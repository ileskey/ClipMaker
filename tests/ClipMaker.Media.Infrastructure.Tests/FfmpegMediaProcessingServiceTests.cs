using System.Security.Cryptography;
using System.Text;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Processing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class FfmpegMediaProcessingServiceTests
{
    private static SignedMediaTime Time(long n, long d = 1)
    { SignedMediaTime.TryCreate(n, d, out var value, out _); return value!; }
    private static PresentationRange Range(long start = 1, long end = 2)
    { PresentationRange.TryCreate(Time(start), Time(end), out var value, out _); return value!; }
    private sealed class Fixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(".artifacts"), "processing-tests", Guid.NewGuid().ToString("N"));
        internal MediaProcessingRequest Request { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            string input = Path.Combine(DirectoryPath, "input.mp4"); File.WriteAllBytes(input, [1, 2, 3]);
            var file = new MediaInputFile(Guid.NewGuid(), input, new(3, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }))));
            var stream = new StreamProcessingPlan(new(file.Id, 0, MediaStreamKind.Video), Range(), Time(-1), "h264", Time(1, 30), 0, 90);
            var frame = new MediaVideoFrameExpectation(30, 1, Time(1, 30), Time(0), Time(1, 30));
            var plan = new MediaProcessingPlan(MediaProcessingMode.Fast, [file], [stream], [frame], 48000);
            Request = new(Guid.NewGuid(), Guid.NewGuid(), plan, Path.Combine(DirectoryPath, "staging.mp4"));
        }
        public void Dispose() { foreach (var path in Directory.GetFiles(DirectoryPath)) File.Delete(path); Directory.Delete(DirectoryPath); }
    }
    private sealed class Locator(bool missing) : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken token) => new(
            missing ? ToolLocationStatus.Missing : ToolLocationStatus.Found, false,
            missing ? null : Path.GetFullPath("ffmpeg.exe"), ToolLocationSource.Path, [], null);
    }
    private static ProcessResult Result(string text = "") => new(ProcessStatus.Exited, 0,
        new(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text), false, false, true, true),
        new(Array.Empty<byte>(), 0, false, false, true, true), null, true, true, true, null);
    private sealed class Runner(string fault, CancellationTokenSource stop) : IProcessRunner
    {
        internal List<ProcessRequest> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? outputReceived, CancellationToken token)
        {
            Calls.Add(request); Assert.Null(outputReceived);
            if (request.Arguments.Contains("-version")) return Task.FromResult(Result("ffmpeg version 8.1 full"));
            using (var output = new FileStream(request.Arguments[^1], FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                output.Write(new byte[] { 9, 8, 7 });
            if (fault == "cancel") stop.Cancel();
            var result = Result();
            return Task.FromResult(fault switch
            {
                "cancel" => result with { Status = ProcessStatus.Cancelled },
                "termination" => result with { Status = ProcessStatus.Cancelled, DescendantExitConfirmed = false },
                "exit" => result with { ExitCode = 1 },
                "stderr" => result with { StandardError = new(Encoding.UTF8.GetBytes("private details"), 15, false, false, true, true) },
                "drain" => result with { StandardOutput = new(Array.Empty<byte>(), 100, true, false, true, true) },
                _ => result
            });
        }
    }
    private sealed class Progress(CancellationTokenSource stop) : IProgress<MediaProcessingProgress>
    { public void Report(MediaProcessingProgress value) { if (value.Percent == 100) stop.Cancel(); } }

    [Theory]
    [InlineData("success")]
    [InlineData("drain")]
    [InlineData("bytes")]
    [InlineData("missing")]
    [InlineData("conflict")]
    [InlineData("cancel")]
    [InlineData("late-cancel")]
    [InlineData("termination")]
    [InlineData("exit")]
    [InlineData("stderr")]
    public async Task CapturesOwnedStagingOrReturnsNullDataWithoutOverwritingUnrelatedFiles(string fault)
    {
        using var f = new Fixture(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault == "bytes") File.WriteAllBytes(f.Request.Plan.Inputs[0].Path, [3, 2, 1]);
        if (fault == "conflict") File.WriteAllText(f.Request.StagingPath, "user");
        var runner = new Runner(fault, stop); var service = new FfmpegMediaProcessingService(new Locator(fault == "missing"), new(), runner);
        var result = await service.ProcessAsync(f.Request, fault == "late-cancel" ? new Progress(stop) : null, stop.Token);
        var resources = service.GetOwnedResources(f.Request.OperationId);
        if (fault is "success" or "drain")
        {
            Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
            Assert.Equal(3, result.Data!.Staging.Content!.Bytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7 })).ToLowerInvariant(), result.Data.Staging.Content.Sha256);
            Assert.Same(f.Request, result.Data.Request);
            Assert.Null(runner.Calls[1].Timeout); Assert.Equal(ProcessOutputMode.Drain, runner.Calls[1].StandardOutputMode);
            Assert.Equal(TimeSpan.FromSeconds(15), runner.Calls[0].Timeout);
            Assert.Single(resources.Files); Assert.True(resources.NeedsCleanup);
            var repeated = await service.ProcessAsync(f.Request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("ProcessingOperationReuse", repeated.Failure!.Field);
        }
        else
        {
            Assert.Null(result.Data);
            if (fault is "cancel" or "late-cancel") { Assert.Equal(MediaOperationStatus.Cancelled, result.Status); Assert.Null(result.Failure); }
            else
            {
                Assert.Equal(fault == "termination" ? MediaFailureKind.TerminationUnconfirmed : fault == "missing" ? MediaFailureKind.MissingTool :
                    fault == "bytes" ? MediaFailureKind.InvalidMedia : fault == "conflict" ? MediaFailureKind.OutputFailed : MediaFailureKind.ProcessFailed,
                    result.Failure!.Kind);
                Assert.DoesNotContain("private", result.Failure.Message);
            }
            if (fault is "bytes" or "missing" or "conflict") Assert.Empty(resources.Files);
            else { Assert.Single(resources.Files); Assert.True(resources.NeedsCleanup); }
            if (fault == "conflict") Assert.Equal("user", File.ReadAllText(f.Request.StagingPath));
            if (fault == "termination")
            {
                Assert.True(resources.HasUnconfirmedProcess);
                var next = new MediaProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), f.Request.Plan, Path.Combine(f.DirectoryPath, "next.mp4"));
                Assert.Equal(MediaFailureKind.TerminationUnconfirmed,
                    (await service.ProcessAsync(next, cancellationToken: TestContext.Current.CancellationToken)).Failure!.Kind);
                Assert.False(File.Exists(next.StagingPath));
            }
        }
        Assert.Equal(fault == "bytes" ? new byte[] { 3, 2, 1 } : [1, 2, 3], File.ReadAllBytes(f.Request.Plan.Inputs[0].Path));
    }

    [Theory]
    [InlineData(9007199254740991L, true)]
    [InlineData(9007199254740992L, false)]
    public void BinderRejectsTimestampExpressionsBeyondExactDoubleIntegers(long ticks, bool supported)
    {
        using var f = new Fixture(); var file = f.Request.Plan.Inputs[0];
        var stream = new StreamProcessingPlan(new(file.Id, 0, MediaStreamKind.Video), Range(), Time(0), "h264", Time(1), 0, ticks);
        var frame = new MediaVideoFrameExpectation(1, 1, Time(1), Time(1), Time(1));
        var plan = new MediaProcessingPlan(MediaProcessingMode.Fast, [file], [stream], [frame], 1);
        var result = new FastCopyCommandBinder().Bind(new(Guid.NewGuid(), Guid.NewGuid(), plan, f.Request.StagingPath));
        Assert.Equal(supported ? MediaOperationStatus.Succeeded : MediaOperationStatus.Failed, result.Status);
        if (!supported) Assert.Equal("NativeClockExpressionUnsupported", result.Failure!.Field);
    }
}
