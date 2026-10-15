namespace ClipMaker.Media.Infrastructure.ProcessExecution;

internal sealed class ProcessOutputCollector(ProcessOutputMode mode)
{
    internal const int FullLimit = 32 * 1024 * 1024;
    internal const int TailLimit = 256 * 1024;
    private readonly object gate = new();
    private readonly MemoryStream? full = mode == ProcessOutputMode.Full ? new() : null;
    private readonly byte[]? tail = mode == ProcessOutputMode.Tail ? new byte[TailLimit] : null;
    private int tailEnd;
    private int tailCount;
    private long total;
    private bool exceeded;
    private bool complete;

    // Return false only for full-output overflow. Tail eviction must never stop draining.
    public bool Add(ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            total = checked(total + bytes.Length);
            if (full is not null)
            {
                if (total > FullLimit) exceeded = true;
                if (!exceeded) full.Write(bytes);
                return !exceeded;
            }
            if (tail is not null)
            {
                if (bytes.Length >= TailLimit)
                {
                    bytes[^TailLimit..].CopyTo(tail);
                    tailEnd = 0;
                    tailCount = TailLimit;
                }
                else
                {
                    int first = Math.Min(bytes.Length, TailLimit - tailEnd);
                    bytes[..first].CopyTo(tail.AsSpan(tailEnd));
                    bytes[first..].CopyTo(tail);
                    tailEnd = (tailEnd + bytes.Length) % TailLimit;
                    tailCount = Math.Min(TailLimit, tailCount + bytes.Length);
                }
            }
            return true;
        }
    }

    public void MarkComplete()
    {
        lock (gate) complete = true;
    }

    public ProcessOutput Snapshot()
    {
        lock (gate)
        {
            byte[] bytes = [];
            if (full is not null && !exceeded) bytes = full.ToArray();
            else if (tail is not null)
            {
                bytes = new byte[tailCount];
                int start = (tailEnd - tailCount + TailLimit) % TailLimit;
                int first = Math.Min(tailCount, TailLimit - start);
                tail.AsSpan(start, first).CopyTo(bytes);
                tail.AsSpan(0, tailCount - first).CopyTo(bytes.AsSpan(first));
            }
            // Full overflow intentionally yields no parsable partial document.
            return new(bytes, total, total > bytes.Length, exceeded, complete && !exceeded, complete);
        }
    }
}
