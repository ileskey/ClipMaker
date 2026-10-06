using ClipDownloader.Application.Contracts;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Diagnostics;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using ClipMaker.Media.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipDownloader.Composition;

internal static class ServiceRegistration
{
    public static ServiceProvider Create(Serilog.ILogger? logger)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logger is not null) builder.AddSerilog(logger, dispose: false);
        });
        services.AddSingleton(new ToolPathSettings());
        services.AddSingleton(new ToolSearchEnvironment(AppContext.BaseDirectory,
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')));
        services.AddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<IToolDiagnosticsService, ToolDiagnosticsService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
