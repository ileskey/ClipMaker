using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ClipDownloader.Composition;
using ClipDownloader.ViewModels;
using ClipDownloader.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipDownloader;

public partial class App : System.Windows.Application
{
    private readonly LoggingSetup logging = new();
    private Serilog.Core.Logger? fileLogger;
    private ServiceProvider? services;
    private MainViewModel? viewModel;
    private DispatcherTimer? logProblemTimer;
    private bool warningShown;
    private bool exiting;
    private bool cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            fileLogger = logging.Create();
            fileLogger?.ForContext<App>().Information("Application starting");
            services = ServiceRegistration.Create(fileLogger);
            viewModel = services.GetRequiredService<MainViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.ShutdownStarted += (_, _) => BeginShutdown();
            window.Show();
            services.GetRequiredService<ILogger<App>>().LogInformation("Application initialized");
            logProblemTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250),
                DispatcherPriority.Background, (_, _) => ShowLogWarning(), Dispatcher);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (exiting) return;
                _ = viewModel.RefreshToolsCommand.ExecuteAsync(null);
                ShowLogWarning();
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            fileLogger?.ForContext<App>().Error(ex, "Required application initialization failed");
            MessageBox.Show("앱을 초기화하지 못했습니다. 실행 환경을 확인해주세요.",
                "ClipDownloader", MessageBoxButton.OK, MessageBoxImage.Error);
            BeginShutdown();
            Shutdown(1);
        }
    }

    private void ShowLogWarning()
    {
        if (exiting || warningShown || Dispatcher.HasShutdownStarted || !logging.HasProblem) return;
        warningShown = true;
        MessageBox.Show(MainWindow, "파일 로그 기록에 문제가 발생했습니다.",
            "ClipDownloader", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BeginShutdown()
    {
        if (exiting) return;
        exiting = true;
        logProblemTimer?.Stop();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BeginShutdown();
        if (!cleanedUp)
        {
            cleanedUp = true;
            try
            {
                services?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                fileLogger?.ForContext<App>().Error(ex, "DI cleanup failed");
            }
            finally
            {
                try
                {
                    try
                    {
                        fileLogger?.ForContext<App>().Information("Application exiting with code {ExitCode}", e.ApplicationExitCode);
                    }
                    finally
                    {
                        fileLogger?.Dispose();
                    }
                }
                finally
                {
                    logging.Stop();
                }
            }
        }
        base.OnExit(e);
    }
}
