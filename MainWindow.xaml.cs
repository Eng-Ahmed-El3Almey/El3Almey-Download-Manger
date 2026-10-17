using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using El3Almey.Services;
using El3Almey.ViewModels;
using El3Almey.Wpf.Views;

namespace El3Almey.Wpf;

public partial class MainWindow : Window
{
    readonly DownloadManager _manager = new();
    readonly DownloadsViewModel _downloads;
    readonly AddVideoViewModel _add;
    readonly HistoryViewModel _history;
    readonly SettingsViewModel _settings = new();

    readonly AddVideoView _addView;
    readonly DownloadsView _downloadsView;
    readonly HistoryView _historyView;
    readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = Loc.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _downloads = new DownloadsViewModel(_manager) { CopyText = t => { try { Clipboard.SetText(t); } catch { } } };
        _add = new AddVideoViewModel(_manager)
        {
            Done = ShowDownloads,
            PickFolder = PickFolder,
            DuplicateFilePrompt = title =>
            {
                var dialog = new DuplicateFileDialog(title, Loc.Language == "ar") { Owner = this };
                return dialog.ShowDialog() == true ? dialog.DownloadWithNewName
                    : dialog.DialogResult == false ? false : null;
            },
            PlaylistLinkPrompt = playlistTitle =>
            {
                bool arabic = Loc.Language == "ar";
                var message = arabic
                    ? $"الرابط لفيديو داخل قائمة التشغيل \"{playlistTitle}\".\n\nنعم: تحميل قائمة التشغيل بالكامل.\nلا: تحميل الفيديو المرتبط فقط.\nإلغاء: الرجوع دون اختيار."
                    : $"This link points to a video inside the playlist \"{playlistTitle}\".\n\nYes: load the full playlist.\nNo: load only the linked video.\nCancel: keep the playlist preview without choosing.";
                var result = MessageBox.Show(this, message,
                    arabic ? "فيديو أم قائمة تشغيل؟" : "Video or playlist?",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                return result == MessageBoxResult.Yes ? true : result == MessageBoxResult.No ? false : null;
            }
        };
        _history = new HistoryViewModel(_manager)
        {
            Confirm = msg => MessageBox.Show(this, msg, "El3Almey Download Manager", MessageBoxButton.YesNo,
                                             MessageBoxImage.Question) == MessageBoxResult.Yes
        };
        _settings.PickFolder = PickFolder;
        _settings.PickFile = () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|*.exe|*.exe" };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };

        DataContext = _downloads;
        _addView = new AddVideoView { DataContext = _add };
        _downloadsView = new DownloadsView { DataContext = _downloads };
        _historyView = new HistoryView { DataContext = _history };
        _settingsView = new SettingsView { DataContext = _settings };

        ShowAdd();                         // the app opens on "Add Video"
        Closing += MainWindow_Closing;
        _manager.Pump();
    }

    private bool _allowCloseAfterConfirmation;

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowCloseAfterConfirmation)
        {
            _manager.Shutdown();
            return;
        }

        bool hasActiveDownloads = _manager.Items.Any(item =>
            item.Status is DownloadStatus.Downloading or DownloadStatus.Queued);

        if (!hasActiveDownloads)
        {
            _manager.Shutdown();
            return;
        }

        bool arabic = Loc.Language == "ar";
        var result = MessageBox.Show(
            this,
            arabic
                ? "يوجد تنزيلات جارية أو في قائمة الانتظار. هل تريد إيقاف التنزيلات وحفظ حالتها وإغلاق البرنامج؟\\n\\nاضغط Yes للإيقاف والإغلاق، أو No للعودة إلى شاشة التنزيلات."
                : "There are active or queued downloads. Do you want to stop them, save their state, and close the application?\\n\\nSelect Yes to stop downloads and close, or No to return to the Downloads screen.",
            arabic ? "التنزيلات ما زالت تعمل" : "Downloads are still active",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _allowCloseAfterConfirmation = true;
            _manager.Shutdown();
            return;
        }

        e.Cancel = true;
        ShowDownloads();
    }

    string? PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }

    void Navigate(System.Windows.Controls.UserControl v, System.Windows.Controls.RadioButton nav, bool search)
    {
        Host.Content = v;
        nav.IsChecked = true;
        SearchBox.Visibility = search ? Visibility.Visible : Visibility.Collapsed;
    }

    void ShowAdd() => Navigate(_addView, NavAdd, false);
    void ShowDownloads() { _downloads.Refresh(); Navigate(_downloadsView, NavDownloads, true); }
    void ShowHistory() { _history.Refresh(); Navigate(_historyView, NavHistory, false); }
    void ShowSettings() => Navigate(_settingsView, NavSettings, false);

    void NavAdd_Click(object s, RoutedEventArgs e) => ShowAdd();
    void NavDownloads_Click(object s, RoutedEventArgs e) => ShowDownloads();
    void NavHistory_Click(object s, RoutedEventArgs e) => ShowHistory();
    void NavSettings_Click(object s, RoutedEventArgs e) => ShowSettings();

    void TitleBar_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) Max_Click(s, e);
        else DragMove();
    }
    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    // ---- keep the maximized window inside the work area (so the taskbar stays visible) ----
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ((HwndSource)PresentationSource.FromVisual(this)).AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024) { AdjustMaxSize(hwnd, lParam); handled = true; }   // WM_GETMINMAXINFO
        return IntPtr.Zero;
    }

    static void AdjustMaxSize(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref info);
            mmi.ptMaxPosition.x = Math.Abs(info.rcWork.left - info.rcMonitor.left);
            mmi.ptMaxPosition.y = Math.Abs(info.rcWork.top - info.rcMonitor.top);
            mmi.ptMaxSize.x = Math.Abs(info.rcWork.right - info.rcWork.left);
            mmi.ptMaxSize.y = Math.Abs(info.rcWork.bottom - info.rcWork.top);
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
}
