using System.Text;
using Serilog;
using Serilog.Debugging;

namespace ClipDownloader.Cli.Composition;

internal sealed class LoggingSetup : IDisposable
{
    private int problem;
    public bool HasProblem => Volatile.Read(ref problem) != 0;
    public Serilog.Core.Logger? Logger { get; private set; }
    public LoggingSetup(string? directory = null)
    {
        SelfLog.Enable(_ => Interlocked.Exchange(ref problem, 1));
        try
        {
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipMaker", "ClipDownloader", "Logs");
            Directory.CreateDirectory(directory);
            Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(directory, "clipdownloader-cli-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true,
                retainedFileCountLimit: 14, shared: true, encoding: new UTF8Encoding(false)).CreateLogger();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { Interlocked.Exchange(ref problem, 1); }
    }
    public void Dispose() { Logger?.Dispose(); SelfLog.Disable(); }
}
