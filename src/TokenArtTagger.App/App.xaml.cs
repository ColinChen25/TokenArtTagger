using System.Windows;
using System.Windows.Threading;
using TokenArtTagger.Core;

namespace TokenArtTagger.App;

public partial class App : System.Windows.Application
{
    public static DebugEventLogger DebugLog { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DebugLog = DebugEventLogger.CreateDefault();
        DebugLog.Write("AppStartup.enter", details: new Dictionary<string, string?>
        {
            ["version"] = AppInfo.Version,
            ["logFile"] = DebugLog.LogFilePath
        });
        base.OnStartup(e);
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        DebugLog.Write("AppStartup.exit");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLog.Write("AppShutdown", details: new Dictionary<string, string?>
        {
            ["exitCode"] = e.ApplicationExitCode.ToString()
        });
        DebugLog.Dispose();
        base.OnExit(e);
    }

    private static async void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DebugLog.Write("DispatcherUnhandledException", details: ExceptionDetails(e.Exception));
        await WriteCrashLogBestEffortAsync(e.Exception);
        System.Windows.MessageBox.Show(
            "TokenArtTagger hit an unexpected problem and recovered where possible. A sanitized diagnostic log was written under LocalAppData\\TokenArtTagger\\Logs.",
            "TokenArtTagger Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            DebugLog.Write("AppDomainUnhandledException", details: ExceptionDetails(exception));
            WriteCrashLogBestEffortAsync(exception).GetAwaiter().GetResult();
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DebugLog.Write("TaskSchedulerUnobservedTaskException", details: ExceptionDetails(e.Exception));
        _ = WriteCrashLogBestEffortAsync(e.Exception);
        e.SetObserved();
    }

    private static async Task WriteCrashLogBestEffortAsync(Exception exception)
    {
        try
        {
            await CrashLogService.WriteCrashLogAsync(exception);
        }
        catch
        {
            // Avoid recursive crashes while reporting a crash.
        }
    }

    private static Dictionary<string, string?> ExceptionDetails(Exception exception)
    {
        return new Dictionary<string, string?>
        {
            ["type"] = exception.GetType().Name,
            ["message"] = exception.Message,
            ["source"] = exception.Source
        };
    }
}
