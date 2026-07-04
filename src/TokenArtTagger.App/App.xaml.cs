using System.Windows;
using System.Windows.Threading;
using TokenArtTagger.Core;

namespace TokenArtTagger.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
    }

    private static async void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
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
            WriteCrashLogBestEffortAsync(exception).GetAwaiter().GetResult();
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
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
}
