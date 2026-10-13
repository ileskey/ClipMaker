using System.Security.Cryptography;
using System.Text;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Downloads;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Xunit;
using F = ClipDownloader.Infrastructure.Tests.AcquisitionContractFixture;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class PresentationVideoProbeTests
{
    private sealed class Locator : IToolLocator
    { public ToolLocationResult Locate(ToolSearchRequest request, CancellationToken token) => new(ToolLocationStatus.Found, false, Path.GetFullPath(request.ExecutableName), ToolLocationSource.Path, [], null); }
    private sealed class Runner(string fault, CancellationTokenSource cancellation) : IProcessRunner
    {
        internal int Calls;
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutputChunk>? callback, CancellationToken token)
        {
            Calls++;
            bool version = request.Arguments.Contains("-version");
            if (!version && fault == "termination") cancellation.Cancel();
            byte[] output = Encoding.UTF8.GetBytes(version ? "ffmpeg version 8.1" : DecodedPictureObservationParserTests.Text);
            byte[] error = fault == "stderr" && !version ? Encoding.UTF8.GetBytes("private detail") : [];
            return Task.FromResult(new ProcessResult(!version && fault == "termination" ? ProcessStatus.TerminationFailed :
                !version && fault == "timeout" ? ProcessStatus.TimedOut : ProcessStatus.Exited, !version && fault == "exit" ? 1 : 0,
                new(output, output.Length, false, false, version || fault != "incomplete", true), new(error, error.Length, false, false, true, true), null,
                true, true, version || fault != "termination", null));
        }
    }
    [Theory]
    [InlineData("success")]
    [InlineData("stderr")]
    [InlineData("termination")]
    [InlineData("timeout")]
    [InlineData("exit")]
    [InlineData("incomplete")]
    [InlineData("identity")]
    public async Task LocalDecodeRequiresSameFileCompleteOutputAndConfirmedProcess(string fault)
    {
        string directory = Path.Combine(Path.GetFullPath(".artifacts"), "presentation-video-probe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "source.mp4");
        byte[] bytes = [1, 2, 3]; File.WriteAllBytes(path, bytes);
        try
        {
            var reference = new IndexedCollectedReference(new(Guid.NewGuid(), path, ["v"]), bytes.Length, bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)), F.Range(0, 2), F.Range(0, 2));
            if (fault == "identity") File.WriteAllBytes(path, [1, 2, 4]);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var runner = new Runner(fault, cts);
            var result = await new PresentationVideoProbe(new Locator(), new(), runner).DecodeAsync(reference, cts.Token);
            if (fault == "success") Assert.Equal(OperationStatus.Succeeded, result.Status);
            else
            {
                Assert.Null(result.Data); Assert.Equal(OperationStatus.Failed, result.Status);
                var code = fault switch
                {
                    "termination" => OperationErrorCode.ProcessTerminationFailed,
                    "timeout" => OperationErrorCode.TimedOut,
                    "exit" => OperationErrorCode.ProcessFailed,
                    "incomplete" => OperationErrorCode.InvalidMetadata,
                    _ => OperationErrorCode.VerificationFailed
                };
                Assert.Equal(code, result.Error!.Code);
                Assert.DoesNotContain("private detail", result.Error.Message);
            }
            Assert.Equal(fault == "identity" ? 0 : 2, runner.Calls);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
