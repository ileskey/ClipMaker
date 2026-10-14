using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using ClipMaker.Media.Core.Models;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class FfprobeMediaProbeTests
{
    private const string Json = """{"streams":[{"index":0,"id":"0x1","codec_type":"video","codec_name":"h264","time_base":"1/6000","r_frame_rate":"60/1","avg_frame_rate":"30/1"}],"frames":[{"stream_index":0,"media_type":"video","duration":100}],"packets":[{"stream_index":0,"pts":-100,"dts":-200,"duration":100,"flags":"K_","size":3,"pos":0}]}""";
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetFullPath(".artifacts"), "shared-probe-tests", Guid.NewGuid().ToString("N"));
        internal MediaProbeRequest Request { get; }
        internal Fixture(MediaProbeDetail detail = MediaProbeDetail.FramesAndPackets, byte[]? content = null, bool inspectContainer = false)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "input.mp4"); byte[] bytes = content ?? [1, 2, 3]; File.WriteAllBytes(path, bytes);
            SignedMediaTime.TryCreate(0, 1, out var start, out _); SignedMediaTime.TryCreate(1, 1, out var end, out _);
            PresentationRange.TryCreate(start!, end!, out var range, out _);
            Request = new(Guid.NewGuid(), Guid.NewGuid(), path, new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), detail, range, inspectContainer);
        }
        public void Dispose() { File.Delete(Request.Path); Directory.Delete(directory); }
    }
    private sealed class Locator(bool missing = false) : IToolLocator
    {
        public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken token) => new(
            missing ? ToolLocationStatus.Missing : ToolLocationStatus.Found, false,
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
    private static ProcessResult Result(string text) => new(ProcessStatus.Exited, 0,
        new(Encoding.UTF8.GetBytes(text), Encoding.UTF8.GetByteCount(text), false, false, true, true),
        new(Array.Empty<byte>(), 0, false, false, true, true), null, true, true, true, null);
    private static FfprobeMediaProbe Probe(Runner runner, bool missing = false) => new(new Locator(missing), new(), runner);

    private static byte[] Box(string type, params byte[][] bodies)
    {
        var body = bodies.SelectMany(b => b).ToArray(); byte[] result = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(result, 4); body.CopyTo(result, 8); return result;
    }
    private static byte[] Header(string type, uint value)
    {
        var body = new byte[type switch { "mvhd" => 100, "tkhd" => 84, _ => 24 }];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), value); return Box(type, body);
    }
    private static byte[] Container(uint track, uint clock) => Box("moov", Header("mvhd", 1000),
        Box("trak", Header("tkhd", track), Box("mdia", Header("mdhd", clock))));

    [Theory]
    [InlineData(1u, 6000u, true)]
    [InlineData(2u, 6000u, false)]
    [InlineData(1u, 48000u, false)]
    public async Task ContainerTrackLinkRequiresExactIdAndClockButHeaderSuccessDoesNotImplyNativeSamples(uint track, uint clock, bool matches)
    {
        using var file = new Fixture(content: Container(track, clock), inspectContainer: true);
        var result = await Probe(new Runner()).ProbeAsync(file.Request, TestContext.Current.CancellationToken);
        Assert.Equal(matches ? MediaOperationStatus.Succeeded : MediaOperationStatus.Failed, result.Status);
        if (!matches) { Assert.Equal(MediaFailureKind.InvalidMedia, result.Failure!.Kind); Assert.Null(result.Data); return; }
        var container = result.Data!.Container!;
        Assert.Equal(1000u, container.Initialization.MovieTimescale);
        Assert.Equal(6000u, Assert.Single(container.Initialization.Tracks).MediaTimescale);
        Assert.Equal(MediaOperationStatus.Failed, container.NativeSamples.Status);
        Assert.Equal(MediaFailureKind.UnsupportedInput, container.NativeSamples.Failure!.Kind);
        Assert.Null(container.NativeSamples.Data);
    }

    [Fact]
    public async Task MalformedContainerFailsBeforeRunningTool()
    {
        using var file = new Fixture(inspectContainer: true); var runner = new Runner();
        var result = await Probe(runner).ProbeAsync(file.Request, TestContext.Current.CancellationToken);
        Assert.Equal(MediaFailureKind.InvalidMedia, result.Failure!.Kind);
        Assert.Null(result.Data); Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task DetailedProbePreservesMissingPtsAndSignedPacketClockWithoutClaimingNativeFacts()
    {
        using var file = new Fixture(); var runner = new Runner();
        var result = await Probe(runner).ProbeAsync(file.Request, TestContext.Current.CancellationToken);
        Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
        Assert.Same(file.Request, result.Data!.Request);
        Assert.Equal("8.1", result.Data.ProbeVersion);
        var raw = result.Data.Observations;
        Assert.Equal(MediaTimeBasis.Demux, raw.TimeBasis);
        Assert.True(raw.FramesObserved); Assert.True(raw.PacketsObserved);
        Assert.Null(Assert.Single(raw.Frames).Pts);
        Assert.Equal(-100, Assert.Single(raw.Packets).Pts);
        Assert.Equal(-200, raw.Packets[0].Dts);
        Assert.NotEqual(raw.Streams[0].DeclaredRate, raw.Streams[0].AverageRate);
        Assert.True(((System.Collections.IList)raw.Frames).IsReadOnly);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.Calls[0].Timeout);
        var command = runner.Calls[1]; Assert.Equal(TimeSpan.FromSeconds(60), command.Timeout);
        Assert.Contains("file", command.Arguments); Assert.Contains("mov", command.Arguments);
        Assert.DoesNotContain("-read_intervals", command.Arguments);
        Assert.Equal(new[] { "-i", file.Request.Path }, command.Arguments.TakeLast(2));
    }

    [Fact]
    public async Task SummaryHasNoImplicitFrameOrPacketObservation()
    {
        using var file = new Fixture(MediaProbeDetail.Summary);
        var runner = new Runner { Respond = r => Result(r.Arguments.Contains("-version") ? "ffprobe version 8.1 full" :
            """{"streams":[{"index":0,"codec_type":"audio","time_base":"1/48000","sample_rate":"48000","channels":2}]}""") };
        var result = await Probe(runner).ProbeAsync(file.Request, TestContext.Current.CancellationToken);
        Assert.Equal(MediaOperationStatus.Succeeded, result.Status);
        Assert.False(result.Data!.Observations.FramesObserved); Assert.False(result.Data.Observations.PacketsObserved);
        Assert.Empty(result.Data.Observations.Frames); Assert.Empty(result.Data.Observations.Packets);
        Assert.DoesNotContain("-show_frames", runner.Calls[1].Arguments);
        Assert.DoesNotContain("-show_packets", runner.Calls[1].Arguments);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("length")]
    [InlineData("missing")]
    [InlineData("cancel")]
    public async Task InvalidIdentityMissingToolAndEarlyCancellationDoNotRunTool(string fault)
    {
        using var file = new Fixture(); var runner = new Runner();
        if (fault == "bytes") File.WriteAllBytes(file.Request.Path, [3, 2, 1]);
        if (fault == "length") File.WriteAllBytes(file.Request.Path, [1]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (fault == "cancel") stop.Cancel();
        var result = await Probe(runner, fault == "missing").ProbeAsync(file.Request, stop.Token);
        Assert.Null(result.Data); Assert.Empty(runner.Calls);
        if (fault == "cancel") { Assert.Equal(MediaOperationStatus.Cancelled, result.Status); Assert.Null(result.Failure); }
        else Assert.Equal(fault == "missing" ? MediaFailureKind.MissingTool : MediaFailureKind.InvalidMedia, result.Failure!.Kind);
    }

    [Theory]
    [InlineData("termination", MediaFailureKind.TerminationUnconfirmed)]
    [InlineData("timeout", MediaFailureKind.TimedOut)]
    [InlineData("limit", MediaFailureKind.InvalidMedia)]
    [InlineData("truncated", MediaFailureKind.InvalidMedia)]
    [InlineData("stderr", MediaFailureKind.InvalidMedia)]
    [InlineData("exit", MediaFailureKind.ProcessFailed)]
    [InlineData("frames-only", MediaFailureKind.InvalidMedia)]
    [InlineData("version", MediaFailureKind.ProcessFailed)]
    public async Task UnsafeOrIncompleteToolResultsNeverReturnData(string fault, MediaFailureKind expected)
    {
        using var file = new Fixture();
        var runner = new Runner { Respond = r =>
        {
            if (r.Arguments.Contains("-version")) return Result(fault == "version" ? "private unrelated text" : "ffprobe version 8.1 full");
            var result = Result(Json);
            return fault switch
            {
                "termination" => result with { Status = ProcessStatus.Cancelled, DescendantExitConfirmed = false },
                "timeout" => result with { Status = ProcessStatus.TimedOut },
                "limit" => result with { StandardOutput = result.StandardOutput with { LimitExceeded = true } },
                "truncated" => result with { StandardOutput = result.StandardOutput with { IsTruncated = true } },
                "stderr" => result with { StandardError = new(Encoding.UTF8.GetBytes("private detail"), 14, false, false, true, true) },
                "exit" => result with { ExitCode = 1 },
                "frames-only" => Result("""{"streams":[{"index":0,"codec_type":"video","time_base":"1/6000"}],"frames":[]}"""),
                _ => result
            };
        } };
        var result = await Probe(runner).ProbeAsync(file.Request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Failure!.Kind); Assert.Null(result.Data);
        Assert.DoesNotContain("private", result.Failure.Message);
    }

    [Theory]
    [InlineData("""{"streams":[{"index":0,"codec_type":"audio","time_base":"1/48000"}],"frames":[],"packets":[{"stream_index":0,"pts":-1024,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":1024,"discard_padding":0,"skip_reason":0,"discard_reason":0}]}]}""", true)]
    [InlineData("""{"streams":[{"index":0,"index":1,"codec_type":"audio","time_base":"1/48000"}],"frames":[],"packets":[]}""", false)]
    [InlineData("""{"streams":[{"index":0,"codec_type":"audio","time_base":"0/1"}],"frames":[],"packets":[]}""", false)]
    public void ParserPreservesSkipAndRejectsAmbiguousFields(string json, bool valid)
    {
        var result = new FfprobeObservationParser().Parse(Encoding.UTF8.GetBytes(json));
        Assert.Equal(valid ? MediaOperationStatus.Succeeded : MediaOperationStatus.Failed, result.Status);
        if (valid) Assert.Equal(1024u, Assert.Single(result.Data!.Packets).Skip!.SkipSamples);
        else Assert.Null(result.Data);
    }
}
