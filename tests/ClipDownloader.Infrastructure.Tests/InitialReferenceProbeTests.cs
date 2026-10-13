using System.Security.Cryptography;
using System.Buffers.Binary;
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
    private const string Json = """{"streams":[{"index":0,"id":"0x7","codec_type":"video","time_base":"1/6000"}],"frames":[],"packets":[]}""";
    private static byte[] Box(string type, params byte[][] bodies)
    {
        byte[] body = bodies.SelectMany(b => b).ToArray(); byte[] result = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(result, 4); body.CopyTo(result, 8); return result;
    }
    private static byte[] Header(string type, uint value)
    {
        var body = new byte[type switch { "mvhd" => 100, "tkhd" => 84, _ => 24 }];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), value); return Box(type, body);
    }
    private static byte[] Media() => Box("moov", Header("mvhd", 1000),
        Box("trak", Header("tkhd", 7), Box("mdia", Header("mdhd", 6000))));
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetFullPath(".artifacts"), "initial-probe-tests", Guid.NewGuid().ToString("N"));
        internal HlsCollectedReference Reference { get; }
        internal Fixture(byte[]? content = null)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "ref.mp4");
            byte[] bytes = content ?? Media(); File.WriteAllBytes(path, bytes);
            SignedMediaTime.TryCreate(0, 1, out var zero, out _);
            SignedMediaTime.TryCreate(2, 1, out var two, out _);
            PresentationRange.TryCreate(zero!, two!, out var range, out _);
            Reference = new(Guid.NewGuid(), new(Guid.NewGuid(), path, ["selected"]), bytes.Length, bytes.Length + 1,
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
        Assert.Equal(1000u, result.Data.ContainerTiming.MovieTimescale);
        var link = Assert.Single(result.Data.TrackLinks);
        Assert.Equal(0, link.StreamIndex); Assert.Equal(7u, link.Track.TrackId);
        Assert.True(((System.Collections.IList)result.Data.TrackLinks).IsReadOnly);
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
        byte[] changed = File.ReadAllBytes(fixture.Reference.File.Path); changed[0] ^= 1;
        File.WriteAllBytes(fixture.Reference.File.Path, length ? [1] : changed);
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

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("clock")]
    [InlineData("extra")]
    [InlineData("empty")]
    public async Task UnmatchedContainerAndProbeNeverPublishLinkedObservation(string fault)
    {
        using var fixture = new Fixture();
        string json = fault switch
        {
            "missing" => Json.Replace("\"id\":\"0x7\",", ""),
            "foreign" => Json.Replace("0x7", "0x1"),
            "clock" => Json.Replace("1/6000", "1/48000"),
            "extra" => Json.Replace("}],\"frames\"", "},{\"index\":1,\"id\":\"0x7\",\"codec_type\":\"audio\",\"time_base\":\"1/6000\"}],\"frames\""),
            _ => "{\"streams\":[],\"frames\":[]}"
        };
        var runner = new Runner { Respond = request => Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : json) };
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Null(result.Data);
        Assert.Equal(fault == "empty" ? OperationErrorCode.InvalidMetadata : OperationErrorCode.VerificationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task ContainerFailureStopsBeforeAnyToolAndPreservesFile()
    {
        using var fixture = new Fixture([1, 2, 3]); var runner = new Runner();
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.InvalidMetadata, result.Error!.Code); Assert.Null(result.Data);
        Assert.Empty(runner.Calls); Assert.True(File.Exists(fixture.Reference.File.Path));
    }
    [Fact]
    public async Task ProbeRequiresPacketObservationAndReturnsMixedPacketOutput()
    {
        using var fixture = new Fixture();
        var runner = new Runner { Respond = request => Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : Json.Replace(",\"packets\":[]", "")) };
        Assert.Equal("PacketObservation", (await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken)).Error!.Field);
        string mixed = Json.Replace("\"frames\":[],\"packets\":[]", "\"packets_and_frames\":[{\"type\":\"packet\",\"stream_index\":0,\"pts\":12000}]");
        runner = new() { Respond = request => Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : mixed) };
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(12000, Assert.Single(result.Data!.Observations.Packets).Pts);
    }

    [Fact]
    public async Task RunnerIoFailureIsDistinctFromFileIdentityFailure()
    {
        using var fixture = new Fixture();
        var runner = new Runner { Respond = _ => throw new IOException("SECRET") };
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(OperationErrorCode.ProcessFailed, result.Error!.Code);
        Assert.Equal("ProbeExecution", result.Error.Field);
        Assert.DoesNotContain("SECRET", result.Error.Message); Assert.Null(result.Data);
        Assert.True(File.Exists(fixture.Reference.File.Path));
    }

    [Fact]
    public async Task FragmentAnchorIsReturnedFromTheSameBoundFile()
    {
        var tfhd = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4), 7);
        var tfdt = new byte[12]; tfdt[0] = 1; BinaryPrimitives.WriteUInt64BigEndian(tfdt.AsSpan(4), 12000);
        byte[] bytes = Media().Concat(Box("moof", Box("traf", Box("tfhd", tfhd), Box("tfdt", tfdt)))).ToArray();
        using var fixture = new Fixture(bytes);
        var result = await Probe(new Runner()).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        Assert.Equal(OperationStatus.Succeeded, result.Status);
        var anchor = Assert.Single(result.Data!.FragmentAnchors);
        Assert.Equal(result.Data.TrackLinks[0].Track.TrackId, anchor.TrackId);
        Assert.Equal(12000ul, anchor.BaseDecodeTime);
        Assert.Same(fixture.Reference, result.Data.Reference);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitIdsDetermineLinksIndependentlyOfListOrder(bool duplicate)
    {
        byte[] media = Box("moov", Header("mvhd", 1000),
            Box("trak", Header("tkhd", 7), Box("mdia", Header("mdhd", 6000))),
            Box("trak", Header("tkhd", 9), Box("mdia", Header("mdhd", 6000))));
        using var fixture = new Fixture(media);
        string json = """{"streams":[{"index":8,"id":"0x9","codec_type":"audio","time_base":"2/12000"},{"index":2,"id":"0x7","codec_type":"video","time_base":"1/6000"}],"frames":[],"packets":[]}""";
        if (duplicate) json = json.Replace("0x9", "0x7");
        var runner = new Runner { Respond = request => Result(request.Arguments.Contains("-version") ? "ffprobe version 8.1" : json) };
        var result = await Probe(runner).ProbeAsync(fixture.Reference, TestContext.Current.CancellationToken);
        if (duplicate) Assert.Equal(OperationErrorCode.VerificationFailed, result.Error!.Code);
        else
        {
            Assert.Equal(OperationStatus.Succeeded, result.Status);
            Assert.Equal(9u, result.Data!.TrackLinks.Single(link => link.StreamIndex == 8).Track.TrackId);
            Assert.Equal(7u, result.Data.TrackLinks.Single(link => link.StreamIndex == 2).Track.TrackId);
        }
    }
}
