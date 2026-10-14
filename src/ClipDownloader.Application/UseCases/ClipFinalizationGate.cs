namespace ClipDownloader.Application.UseCases;

internal sealed class ClipFinalizationGate : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationToken token;
    private readonly CancellationTokenRegistration registration;
    private bool cancelled, finalizing;
    internal ClipFinalizationGate(CancellationToken token)
    { this.token = token; registration = token.Register(() => { lock (gate) if (!finalizing) cancelled = true; }); }
    internal bool TryBeginFinalizing()
    { lock (gate) { if (cancelled || token.IsCancellationRequested) return false; finalizing = true; return true; } }
    public void Dispose() => registration.Dispose();
}
