using Microsoft.Toolkit.Uwp.Notifications;

namespace El3Almey.Services;

public static class WindowsToastNotifications
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        try
        {
            // Initializing the compatibility manager registers this unpackaged Win32 app
            // with Windows so notifications can appear in Notification Center.
            ToastNotificationManagerCompat.OnActivated += _ => { };
            _initialized = true;
        }
        catch
        {
            // Notification failures must never prevent the download manager from starting.
        }
    }

    public static void Show(string title, string message)
    {
        try
        {
            Initialize();
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            try
            {
                var logDir = Path.Combine(SettingsStore.DataDir, "logs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "notifications.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Windows toast failed: {ex}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
