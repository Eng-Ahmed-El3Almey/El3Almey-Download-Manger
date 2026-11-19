using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using El3Almey.Services;

namespace El3Almey.Wpf.Views;

public partial class StartupWindow : Window
{
    private bool _allowClose;
    private bool _working;
    private readonly CancellationTokenSource _cancellation = new();

    public StartupWindow()
    {
        InitializeComponent();
        bool arabic = SettingsStore.Current.Language == "ar";
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (arabic)
        {
            VersionText.Text = "فحص مكونات التشغيل";
            HeadingText.Text = "جاري فحص الأدوات المطلوبة";
            DescriptionText.Text = "يرجى الانتظار حتى ينتهي البرنامج من التحقق من الأدوات الأساسية.";
            StatusText.Text = "جاري فحص FFmpeg و yt-dlp...";
            InstallButton.Content = "تنزيل الأدوات الناقصة";
            RetryButton.Content = "إعادة الفحص";
            ExitActionButton.Content = "خروج";
        }
    }

    private async void StartupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckToolsAsync();
    }

    private async Task CheckToolsAsync()
    {
        SetWorking(true);
        HeadingText.Text = SettingsStore.Current.Language == "ar" ? "جاري فحص الأدوات المطلوبة" : "Checking required components";
        DescriptionText.Text = SettingsStore.Current.Language == "ar"
            ? "يتم التأكد من وجود الأدوات وإمكانية تشغيلها قبل فتح البرنامج."
            : "The application checks that every required tool exists and can run before opening.";
        StatusText.Text = SettingsStore.Current.Language == "ar" ? "جاري التحقق من FFmpeg و yt-dlp..." : "Verifying FFmpeg and yt-dlp...";
        StartupProgressBar.IsIndeterminate = true;
        PercentText.Text = "";
        MissingToolsText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var missing = await ToolBootstrapService.CheckAsync(_cancellation.Token);
            if (missing.Count == 0)
            {
                StatusText.Text = SettingsStore.Current.Language == "ar" ? "تم التحقق من جميع الأدوات بنجاح." : "All required tools passed verification.";
                StartupProgressBar.IsIndeterminate = false;
                StartupProgressBar.Value = 100;
                await Task.Delay(350, _cancellation.Token);
                OpenMainWindow();
                return;
            }

            bool arabic = SettingsStore.Current.Language == "ar";
            HeadingText.Text = arabic ? "بعض الأدوات غير متوفرة" : "Required tools are missing";
            DescriptionText.Text = arabic
                ? "لن يتم فتح البرنامج قبل تثبيت الأدوات المطلوبة والتحقق من تشغيلها."
                : "The application cannot continue until the missing tools are installed and verified.";
            MissingToolsText.Text = (arabic ? "الأدوات الناقصة: " : "Missing tools: ") + string.Join(", ", missing);
            MissingToolsText.Visibility = Visibility.Visible;
            StatusText.Text = arabic ? "في انتظار تأكيدك." : "Waiting for your confirmation.";
            StartupProgressBar.IsIndeterminate = false;
            StartupProgressBar.Value = 0;
            PercentText.Text = "";
            InstallButton.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Collapsed;
            ExitActionButton.Visibility = Visibility.Visible;
            SetWorking(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        SetWorking(true);
        InstallButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        ExitActionButton.Visibility = Visibility.Collapsed;
        MissingToolsText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;

        bool arabic = SettingsStore.Current.Language == "ar";
        HeadingText.Text = arabic ? "جاري تجهيز الأدوات المطلوبة" : "Preparing required tools";
        DescriptionText.Text = arabic
            ? "يتم تنزيل الأدوات الناقصة فقط، مع متابعة حجم التنزيل والتحقق من كل ملف بعد اكتماله."
            : "Only missing tools are downloaded. Each file is verified after the download completes.";
        StartupProgressBar.IsIndeterminate = false;
        StartupProgressBar.Value = 0;
        PercentText.Text = "0%";

        var progress = new Progress<StartupProgress>(p =>
        {
            StatusText.Text = p.Message;
            StartupProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
            PercentText.Text = $"{Math.Clamp(p.Percent, 0, 100):0}%";
        });

        try
        {
            await ToolBootstrapService.PrepareAsync(progress, _cancellation.Token);
            StatusText.Text = arabic ? "اكتمل الفحص بنجاح. جاري فتح البرنامج..." : "Verification complete. Opening the application...";
            StartupProgressBar.Value = 100;
            PercentText.Text = "100%";
            await Task.Delay(400, _cancellation.Token);
            OpenMainWindow();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryButton.Visibility = Visibility.Collapsed;
        await CheckToolsAsync();
    }

    private void ShowFailure(Exception exception)
    {
        bool arabic = SettingsStore.Current.Language == "ar";
        SetWorking(false);
        HeadingText.Text = arabic ? "تعذر تجهيز الأدوات" : "Tool verification failed";
        DescriptionText.Text = arabic
            ? "لن يتم فتح البرنامج لأن إحدى الأدوات المطلوبة لم تجتز الفحص."
            : "The application will remain closed because a required tool failed verification.";
        StatusText.Text = arabic ? "فشل التحقق." : "Verification failed.";
        string diagnostic = exception.ToString();
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "El3AlmeyDownloadManager", "Logs", "startup-error.log");

        try
        {
            string? logDirectory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDirectory))
                Directory.CreateDirectory(logDirectory);

            File.WriteAllText(logPath,
                $"Startup failure: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\\r\\n\\r\\n{diagnostic}");
        }
        catch (Exception logException)
        {
            diagnostic += Environment.NewLine + Environment.NewLine
                + "Could not write diagnostic log: " + logException;
            logPath = "";
        }

        ErrorText.Text = diagnostic
            + (string.IsNullOrEmpty(logPath) ? "" : Environment.NewLine + Environment.NewLine
                + (arabic ? "ملف تفاصيل الخطأ: " : "Diagnostic log: ") + logPath);
        ErrorText.Visibility = Visibility.Visible;
        StartupProgressBar.IsIndeterminate = false;
        InstallButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        ExitActionButton.Visibility = Visibility.Visible;
    }

    private void SetWorking(bool working)
    {
        _working = working;
        InstallButton.IsEnabled = !working;
        RetryButton.IsEnabled = !working;
        ExitActionButton.IsEnabled = !working;
    }

    private void OpenMainWindow()
    {
        _allowClose = true;
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();

        // Notify the user when startup finishes while another application is in the foreground.
        main.Dispatcher.BeginInvoke(new Action(() => FlashTaskbarIfNotForeground(main)));

        Close();
    }

    private static void FlashTaskbarIfNotForeground(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || GetForegroundWindow() == handle)
            return;

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = handle,
            Flags = FlashTaskbar,
            Count = 6,
            Timeout = 0
        };

        FlashWindowEx(ref info);
    }

    private const uint FlashTaskbar = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        _cancellation.Cancel();
        Application.Current.Shutdown();
    }

    private void StartupWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (!_working) ExitButton_Click(this, new RoutedEventArgs());
    }
}