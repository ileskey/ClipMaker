using System.Diagnostics;
using System.IO;
using System.Text;
using Serilog;
using Serilog.Debugging;

namespace ClipDownloader.Composition;

internal sealed class LoggingSetup
{
    private int problemDetected;
    public bool HasProblem => Volatile.Read(ref problemDetected) != 0;

    public LoggerConfiguration Configure(string directory) => new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.File(Path.Combine(directory, "clipdownloader-.log"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 14,
            shared: true,
            encoding: new UTF8Encoding(false),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");

    public Serilog.Core.Logger? Create()
    {
        SelfLog.Enable(ReportProblem);
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClipMaker", "ClipDownloader", "Logs");
            Directory.CreateDirectory(directory);
            return Configure(directory).CreateLogger();
        }
        catch (Exception ex)
        {
            ReportProblem(ex.ToString());
            return null;
        }
    }

    private void ReportProblem(string message)
    {
        Debug.WriteLine(message);
        Interlocked.Exchange(ref problemDetected, 1);
    }

    public void Stop() => SelfLog.Disable();
}
