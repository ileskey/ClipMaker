using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using Xunit;

namespace ClipMaker.Media.Infrastructure.Tests;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ClipMaker-Process-" + Guid.NewGuid());
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ProcessFixture", "ClipMaker.ProcessFixture.exe");

    public ProcessRunnerTests() => Directory.CreateDirectory(root);

    private ProcessRequest Request(IEnumerable<string> arguments,
        ProcessOutputMode stdout = ProcessOutputMode.Full, ProcessOutputMode stderr = ProcessOutputMode.Full) =>
        new(Fixture, arguments, root, stdout, stderr);

    private static string Text(ProcessOutput output) => new UTF8Encoding(false, true).GetString(output.Bytes.Span);

    private static Task<ProcessResult> Run(ProcessRequest request, Action<ProcessOutputChunk>? callback = null) =>
        new ProcessRunner().RunAsync(request, callback, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task ExitedKeepsToolExitCodeSeparateAndCapturesBothStreams(int exitCode)
    {
        var result = await Run(Request(["exit", exitCode.ToString()]));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Null(result.Failure);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.Null(result.DescendantExitConfirmed);
        Assert.Equal("stdout before exit", Text(result.StandardOutput));
        Assert.Equal("stderr before exit", Text(result.StandardError));
    }

    [Fact]
    public async Task ArgumentListPreservesWhitespaceQuotesEmptyAndShellMetacharacters()
    {
        string[] arguments = ["a b", "", "\"quoted\"", "한글😀", "& echo unexpected", "$(echo unexpected)", "trailing\\"];
        var result = await Run(Request(new[] { "arguments" }.Concat(arguments)));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(arguments, JsonSerializer.Deserialize<string[]>(Text(result.StandardOutput)));
    }

    [Fact]
    public async Task UsesSpecifiedWorkingDirectory()
    {
        var result = await Run(Request(["directory"]));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(Path.GetFullPath(root), Text(result.StandardOutput));
    }

    [Theory]
    [InlineData(ProcessOutputMode.Full)]
    [InlineData(ProcessOutputMode.Tail)]
    [InlineData(ProcessOutputMode.Drain)]
    public async Task DrainsBothBusyStreamsAndAppliesIndependentCapturePolicies(ProcessOutputMode mode)
    {
        const int length = 256 * 1024 + 17;
        var result = await Run(Request(["dual", length.ToString()], mode, mode));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(length, result.StandardOutput.TotalBytes);
        Assert.Equal(length, result.StandardError.TotalBytes);
        int expectedLength = mode == ProcessOutputMode.Full ? length : mode == ProcessOutputMode.Tail ? 256 * 1024 : 0;
        Assert.Equal(expectedLength, result.StandardOutput.Bytes.Length);
        Assert.Equal(expectedLength, result.StandardError.Bytes.Length);
        Assert.All(result.StandardOutput.Bytes.ToArray(), value => Assert.Equal((byte)'O', value));
        Assert.All(result.StandardError.Bytes.ToArray(), value => Assert.Equal((byte)'E', value));
        Assert.True(result.StandardOutput.IsComplete);
        Assert.True(result.StandardError.IsComplete);
    }

    [Fact]
    public async Task TailKeepsDrainingFortyMiBPerStreamWithoutFullAccumulation()
    {
        const int length = 40 * 1024 * 1024;
        var result = await Run(Request(["dual", length.ToString()], ProcessOutputMode.Tail, ProcessOutputMode.Tail));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(length, result.StandardOutput.TotalBytes);
        Assert.Equal(length, result.StandardError.TotalBytes);
        Assert.Equal(256 * 1024, result.StandardOutput.Bytes.Length);
        Assert.Equal(256 * 1024, result.StandardError.Bytes.Length);
        Assert.True(result.StandardOutput.IsTruncated);
        Assert.True(result.StandardError.IsTruncated);
    }

    [Fact]
    public async Task FullAllowsExactly32MiB()
    {
        var result = await Run(Request(["bytes", (32 * 1024 * 1024).ToString()]));
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(32 * 1024 * 1024, result.StandardOutput.Bytes.Length);
        Assert.False(result.StandardOutput.LimitExceeded);
        Assert.True(result.StandardOutput.IsComplete);
    }

    [Fact]
    public async Task FullOverflowIsExplicitFailureAndNeverReturnsPartialJson()
    {
        var result = await Run(Request(["bytes", (32 * 1024 * 1024 + 1).ToString()]));
        Assert.Equal(ProcessStatus.ExecutionFailed, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal(ProcessFailureKind.OutputLimitExceeded, result.Failure.Kind);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.StreamsDrained);
        Assert.True(result.StandardOutput.LimitExceeded);
        Assert.False(result.StandardOutput.IsComplete);
        Assert.Empty(result.StandardOutput.Bytes.ToArray());
    }

    [Fact]
    public async Task CallbackDecodingPreservesSplitUtf8AndIsChunkedWithoutWaitingForLines()
    {
        var chunks = new ConcurrentQueue<ProcessOutputChunk>();
        var result = await Run(Request(["unicode"]), chunks.Enqueue);
        Assert.Equal(ProcessStatus.Exited, result.Status);
        string expected = new string('a', 8191) + "한😀\r\n끝";
        Assert.Equal(expected, Text(result.StandardOutput));
        Assert.Equal(expected, string.Concat(chunks.Select(chunk => chunk.Text)));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Text.Length, 1, 8192));
        Assert.Equal(Encoding.UTF8.GetByteCount(expected), result.StandardOutput.TotalBytes);
    }

    [Fact]
    public async Task RawCaptureCountsBomAndInvalidBytesWhileCallbackFlushesIncompleteUtf8()
    {
        var chunks = new ConcurrentQueue<ProcessOutputChunk>();
        var result = await Run(Request(["invalid-utf8"]), chunks.Enqueue);
        Assert.Equal(ProcessStatus.Exited, result.Status);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf, 0xff, 0xe2 }, result.StandardOutput.Bytes.ToArray());
        Assert.Equal(5, result.StandardOutput.TotalBytes);
        Assert.Throws<DecoderFallbackException>(() => Text(result.StandardOutput));
        Assert.Equal("\uFEFF\uFFFD\uFFFD", string.Concat(chunks.Select(chunk => chunk.Text)));
    }

    [Fact]
    public async Task ReceiverFailureIsReportedAndProcessIsCleanedUp()
    {
        var result = await Run(Request(["dual", (40 * 1024 * 1024).ToString()], ProcessOutputMode.Tail, ProcessOutputMode.Tail),
            _ => throw new InvalidOperationException("receiver fixture failure"));
        Assert.Equal(ProcessStatus.ExecutionFailed, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal(ProcessFailureKind.CallbackFailed, result.Failure.Kind);
        Assert.IsType<InvalidOperationException>(result.Failure.Exception);
        Assert.True(result.RootExitConfirmed);
        Assert.True(result.StreamsDrained);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrUnexecutableFileIsStartFailureWithNoInventedExitCode(bool createFile)
    {
        string path = Path.Combine(root, "invalid.exe");
        if (createFile) File.WriteAllText(path, "This is not an executable.");
        var result = await Run(new(path, [], root));
        Assert.Equal(ProcessStatus.StartFailed, result.Status);
        Assert.Null(result.ExitCode);
        Assert.NotNull(result.Failure);
        Assert.Equal(ProcessFailureKind.StartFailed, result.Failure.Kind);
        Assert.Equal(0, result.StandardOutput.TotalBytes);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotAttemptToStartMissingExecutable()
    {
        var request = new ProcessRequest(Path.Combine(root, "missing.exe"), [], root);
        var result = await new ProcessRunner().RunAsync(request, null, new CancellationToken(true));
        Assert.Equal(ProcessStatus.Cancelled, result.Status);
        Assert.Null(result.ExitCode);
        Assert.True(result.DescendantExitConfirmed);
    }

    [Fact]
    public void RequestSnapshotsArguments()
    {
        var arguments = new List<string> { "exit", "0" };
        var request = Request(arguments);
        arguments[1] = "7";
        Assert.Equal("0", request.Arguments[1]);
    }

    [Fact]
    public void RequestRejectsRelativePathsInvalidModesArgumentsAndTimeouts()
    {
        Assert.Throws<ArgumentException>(() => new ProcessRequest("relative.exe", [], root));
        Assert.Throws<ArgumentException>(() => new ProcessRequest(Fixture, [], "relative"));
        Assert.Throws<ArgumentException>(() => new ProcessRequest(Fixture, ["bad\0arg"], root));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessRequest(Fixture, [], root, (ProcessOutputMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessRequest(Fixture, [], root, timeout: TimeSpan.Zero));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
