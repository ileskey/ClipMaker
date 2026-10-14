using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.UseCases;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipDownloader.Infrastructure.Downloads;
using ClipDownloader.Infrastructure.FileSystem;
using ClipDownloader.Infrastructure.Sources;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipMaker.Media.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace ClipDownloader.Cli.Composition;

public static class ServiceRegistration
{
    public static ServiceProvider Create(CliCommand command, ICliConsole console, Serilog.ILogger? logger)
    {
        string cwd = Environment.CurrentDirectory;
        var services = new ServiceCollection();
        services.AddSingleton(console);
        services.AddSingleton(new ToolPathSettings(command.ToolPaths.ToDictionary(p => p.Key, p => (string?)p.Value)));
        services.AddSingleton(new ToolSearchEnvironment(AppContext.BaseDirectory,
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)));
        services.AddSingleton<IToolLocator>(s => new ToolLocator(s.GetRequiredService<ToolSearchEnvironment>()));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IToolExecutionDiagnostics>(s => new ToolExecutionDiagnostics(s.GetRequiredService<IToolLocator>(),
            s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>(), cwd));
        services.AddSingleton<IVideoInfoService>(s => new YtDlpVideoInfoService(s.GetRequiredService<IToolExecutionDiagnostics>(),
            s.GetRequiredService<IToolLocator>(), s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>(), cwd));
        services.AddSingleton<IVideoSourceParser, SupportedVideoSourceParser>();
        services.AddSingleton<IOutputPathValidator>(_ => new OutputPathValidator(cwd));
        services.AddSingleton<IOutputWorkspace>(s => new OutputWorkspace(s.GetRequiredService<IOutputPathValidator>()));
        // The owner is registered once; resource queries and all source operations use it.
        services.AddSingleton<SelectedSourceAcquisitionService>(s => new(s.GetRequiredService<IToolExecutionDiagnostics>(),
            s.GetRequiredService<IToolLocator>(), s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>(), cwd));
        services.AddSingleton<IClipMediaService>(s => new ClipMediaService(new FfprobeMediaProbe(s.GetRequiredService<IToolLocator>(),
            s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>()), s.GetRequiredService<IToolLocator>(),
            s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>()));
        SignedMediaTime.TryCreate(60, 1, out var span, out _);
        var budget = new SourceInspectionBudget(64 * 1024 * 1024, span!);
        services.AddSingleton<IClipJobService>(s =>
        {
            var source = s.GetRequiredService<SelectedSourceAcquisitionService>();
            return new ClipJobService(s.GetRequiredService<IVideoInfoService>(), s.GetRequiredService<IVideoSourceParser>(),
                s.GetRequiredService<IToolExecutionDiagnostics>(), s.GetRequiredService<IOutputPathValidator>(),
                s.GetRequiredService<IOutputWorkspace>(), source, source, source, s.GetRequiredService<IClipMediaService>(), budget);
        });
        services.AddSingleton<CliApplication>(s => new(s.GetRequiredService<IToolExecutionDiagnostics>(), s.GetRequiredService<IVideoInfoService>(),
            s.GetRequiredService<IClipJobService>(), s.GetRequiredService<IOutputPathValidator>(), console,
            progress => logger?.Information("Job {JobId} stage {Stage}", progress.JobId, progress.Stage)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
