using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
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
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                var values = ToastArguments.Parse(args.Argument);
                var action = values.TryGetValue("action", out var actionValue) ? actionValue : "";
                var path = values.TryGetValue("path", out var pathValue) ? pathValue : "";

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(path)) return;

                        if (action == "open-folder")
                        {
                            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

                            if (File.Exists(path))
                                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                            else
                                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                        }
                        else if (action == "play")
                        {
                            if (File.Exists(path))
                                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        }
                    }
                    catch (Exception ex) { LogFailure("Toast action failed", ex); }
                }));
            };
            _initialized = true;
        }
        catch (Exception ex) { LogFailure("Toast initialization failed", ex); }
    }

    public static void Show(string title, string message, string filePath, string outputFolder)
    {
        try
        {
            Initialize();

            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(message);

            var logoPath = CreateAppLogo();
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                builder.AddAppLogoOverride(new Uri(logoPath), ToastGenericAppLogoCrop.Circle);

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                builder.AddButton(new ToastButton()
                    .SetContent("Play video")
                    .AddArgument("action", "play")
                    .AddArgument("path", filePath));
            }

            var folderTarget = !string.IsNullOrWhiteSpace(filePath) ? filePath : outputFolder;
            if (!string.IsNullOrWhiteSpace(folderTarget))
            {
                builder.AddButton(new ToastButton()
                    .SetContent("Show in folder")
                    .AddArgument("action", "open-folder")
                    .AddArgument("path", folderTarget));
            }

            builder.Show();
        }
        catch (Exception ex) { LogFailure("Windows toast failed", ex); }
    }

    private static string? CreateAppLogo()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) return null;

            var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
            bitmap.Freeze();

            var logoPath = Path.Combine(SettingsStore.DataDir, "notification-app-logo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(logoPath)!);
            using var stream = File.Create(logoPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            icon.Dispose();
            return logoPath;
        }
        catch (Exception ex) { LogFailure("App logo creation failed", ex); return null; }
    }

    private static void LogFailure(string message, Exception ex)
    {
        try
        {
            var logDir = Path.Combine(SettingsStore.DataDir, "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "notifications.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}: {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
