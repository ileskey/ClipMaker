namespace ClipMaker.Media.Infrastructure.ProcessExecution;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request,
        Action<ProcessOutputChunk>? outputReceived, CancellationToken cancellationToken);
}
