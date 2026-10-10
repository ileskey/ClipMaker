namespace ClipMaker.Media.Infrastructure.ProcessExecution;

// The runner owns a session; the platform session owns handles, pipes and process containment.
internal interface IProcessSession : IDisposable
{
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    Task RootExit { get; }
    bool RootHasExited { get; }
    int? ExitCode { get; }
    bool AllProcessesExited { get; }
    void Terminate();
}
