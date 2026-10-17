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
                    ? (string.IsNullOrWhiteSpace(playlistTitle)
                        ? "الرابط يحتوي على فيديو داخل قائمة تشغيل.\n\nنعم: تحميل قائمة التشغيل بالكامل.\nلا: تحميل الفيديو فقط.\nإلغاء: إلغاء العملية."
                        : $"الرابط لفيديو داخل قائمة التشغيل \"{playlistTitle}\".\n\nنعم: تحميل قائمة التشغيل بالكامل.\nلا: تحميل الفيديو المرتبط فقط.\nإلغاء: إلغاء العملية.")
                    : (string.IsNullOrWhiteSpace(playlistTitle)
                        ? "This link contains a video that belongs to a playlist.\n\nYes: download the full playlist.\nNo: download only this video.\nCancel: cancel this action."
                        : $"This link points to a video inside the playlist \"{playlistTitle}\".\n\nYes: load the full playlist.\nNo: load only the linked video.\nCancel: cancel this action.");
                var result = MessageBox.Show(this, message,
                    arabic ? "فيديو أم قائمة تشغيل؟" : "Video or playlist?",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                return result == MessageBoxResult.Yes ? true : result == MessageBoxResult.No ? false : null;
            }
        };
        _history = new HistoryViewModel(_manager)
        {
            Confirm = msg => new ClearHistoryDialog(msg, Loc.Language == "ar") { Owner = this }.ShowDialog() == true
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
        _manager.DownloadCompleted += Manager_DownloadCompleted;
        _manager.Pump();
    }

    private bool _allowCloseAfterConfirmation;
    private bool _closeConfirmationOpen;

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowCloseAfterConfirmation) { _manager.Shutdown(); return; }
        bool hasActiveDownloads = _manager.Items.Any(item => item.Status is DownloadStatus.Downloading or DownloadStatus.Queued);
        if (!hasActiveDownloads) { _manager.Shutdown(); return; }
        e.Cancel = true;
        if (_closeConfirmationOpen) return;
        bool arabic = Loc.Language == "ar";
        CloseConfirmTitle.Text = arabic ? "التنزيلات ما زالت تعمل" : "Downloads are still active";
        CloseConfirmMessage.Text = arabic
            ? "يوجد تنزيلات جارية أو في قائمة الانتظار. هل تريد إيقاف التنزيلات وحفظ حالتها وإغلاق البرنامج؟"
            : "There are active or queued downloads. Do you want to stop them, save their state, and close the application?";
        CloseConfirmCancelButton.Content = arabic ? "لا، رجوع" : "No, Go Back";
        CloseConfirmYesButton.Content = arabic ? "نعم، إغلاق" : "Yes, Close";
        CloseConfirmOverlay.Visibility = Visibility.Visible;
        _closeConfirmationOpen = true;
    }

    private void CloseConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseConfirmOverlay.Visibility = Visibility.Collapsed;
        _closeConfirmationOpen = false;
        ShowDownloads();
    }

    private void CloseConfirmYes_Click(object sender, RoutedEventArgs e)
    {
        CloseConfirmOverlay.Visibility = Visibility.Collapsed;
        _closeConfirmationOpen = false;
        _allowCloseAfterConfirmation = true;
        _manager.Shutdown();
        Close();
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

    void Manager_DownloadCompleted(DownloadItem item)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var toast = new DownloadCompleteToast(item, Loc.Language == "ar");
            toast.Show();
        }));
    }

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
