using System.Globalization;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Cli;

public sealed class ConsoleProgress(ICliConsole console, Action<JobProgress>? stageLog = null) : IProgress<JobProgress>
{
    private readonly object gate = new();
    private JobStage? last;
    private int previousWidth;
    public void Report(JobProgress value)
    {
        lock (gate)
        {
            bool changed = last != value.Stage;
            if (changed) { last = value.Stage; stageLog?.Invoke(value); }
            if (console.ErrorRedirected)
            {
                if (changed) console.Error($"[{value.Stage}]{(value.Stage == JobStage.Completed ? " 결과 반환 중" : " 진행 중")}\n");
                return;
            }
            string percent = value.Stage == JobStage.Completed ? " 결과 반환 중" :
                value.Percent is { } number ? $" {number.ToString("0.0", CultureInfo.InvariantCulture)}%" : " 진행 중";
            string line = $"[{value.Stage}]{percent}";
            console.Error("\r" + line.PadRight(previousWidth));
            previousWidth = line.Length;
        }
    }
    public void Finish()
    {
        lock (gate) if (!console.ErrorRedirected && last is not null) console.Error("\n");
    }
}
