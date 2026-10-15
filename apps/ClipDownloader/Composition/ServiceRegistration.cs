using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Application.UseCases;
using ClipDownloader.Dialogs;
using ClipDownloader.Infrastructure.Downloads;
using ClipDownloader.Infrastructure.FileSystem;
using ClipDownloader.Infrastructure.Sources;
using ClipDownloader.Infrastructure.YtDlp;
using ClipMaker.Media.Core.ValueObjects;
using ClipMaker.Media.Infrastructure.Probing;
using ClipMaker.Media.Infrastructure.ProcessExecution;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using ClipMaker.Media.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipDownloader.Composition;

public static class ServiceRegistration
{
    public static ServiceProvider Create(Serilog.ILogger? logger)
    {
        var services = new ServiceCollection();
        string cwd = Environment.CurrentDirectory;
        services.AddLogging(builder =>
        {
            if (logger is not null) builder.AddSerilog(logger, dispose: false);
        });
        services.AddSingleton(new ToolPathSettings());
        services.AddSingleton(new ToolSearchEnvironment(AppContext.BaseDirectory,
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')));
        services.AddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<IToolDiagnosticsService, ToolDiagnosticsService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IToolExecutionDiagnostics>(s => new ToolExecutionDiagnostics(s.GetRequiredService<IToolLocator>(),
            s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>(), cwd));
        services.AddSingleton<IVideoInfoService>(s => new YtDlpVideoInfoService(s.GetRequiredService<IToolExecutionDiagnostics>(),
            s.GetRequiredService<IToolLocator>(), s.GetRequiredService<ToolPathSettings>(), s.GetRequiredService<IProcessRunner>(), cwd));
        services.AddSingleton<IVideoSourceParser, SupportedVideoSourceParser>();
        services.AddSingleton<IOutputPathValidator>(_ => new OutputPathValidator(cwd));
        services.AddSingleton<IOutputWorkspace>(s => new OutputWorkspace(s.GetRequiredService<IOutputPathValidator>()));
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
        services.AddSingleton<IOutputFileDialog, OutputFileDialog>();
        services.AddSingleton<IThumbnailContentService, Infrastructure.Thumbnails.HttpThumbnailContentService>();
        services.AddSingleton<Imaging.IThumbnailDecoder, Imaging.WpfThumbnailDecoder>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
