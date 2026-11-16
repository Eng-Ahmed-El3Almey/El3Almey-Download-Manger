using System.Windows;
using System.Windows.Threading;
using System.Text;
using El3Almey.Services;
using El3Almey.Wpf.Views;

namespace El3Almey.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        SettingsStore.Load();
        Loc.Apply(SettingsStore.Current.Language);
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        var startupWindow = new StartupWindow();
        MainWindow = startupWindow;
        startupWindow.Show();
    }

    private static string CrashLogPath => Path.Combine(SettingsStore.DataDir, "crash.log");

    private static void LogException(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDir);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}{Environment.NewLine}" +
                (exception?.ToString() ?? "No exception details available") + Environment.NewLine +
                new string('-', 80) + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* Crash logging must never cause another crash. */ }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("WPF Dispatcher unhandled exception", e.Exception);
        // Do not mark handled: preserving normal failure behavior avoids hiding a corrupted UI state.
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogException($"AppDomain unhandled exception (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved task exception", e.Exception);
    }
}
