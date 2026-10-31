using System.Runtime.InteropServices;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    readonly ShortcutsView _shortcutsView;

    public MainWindow()
    {
        InitializeComponent();\n        WindowsToastNotifications.Initialize();
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
                var dialog = new PlaylistLinkDialog(playlistTitle, Loc.Language == "ar") { Owner = this };
                return dialog.ShowDialog() == true ? dialog.DownloadPlaylist
                    : dialog.DialogResult == false ? false : null;
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
        _shortcutsView = new ShortcutsView();

        ShowAdd();                         // the app opens on "Add Video"
        Closing += MainWindow_Closing;
        _manager.DownloadCompleted += Manager_DownloadCompleted;
        _manager.AllDownloadsFinished += Manager_AllDownloadsFinished;
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
        nav.IsChecked = true;
        SearchBox.Visibility = search ? Visibility.Visible : Visibility.Collapsed;

        // Fade each page in to make navigation feel smooth without rebuilding the view.
        Host.BeginAnimation(UIElement.OpacityProperty, null);
        Host.Opacity = 0;
        Host.Content = v;
        Host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    void ShowAdd() => Navigate(_addView, NavAdd, false);
    void ShowDownloads() { _downloads.Refresh(); Navigate(_downloadsView, NavDownloads, true); }
    void ShowHistory() { _history.Refresh(); Navigate(_historyView, NavHistory, false); }
    void ShowSettings() => Navigate(_settingsView, NavSettings, false);
    void ShowShortcuts() => Navigate(_shortcutsView, NavShortcuts, false);

    void Manager_DownloadCompleted(DownloadItem item)
    {
        var settings = SettingsStore.Current;
        string title = item.IsPlaylist
            ? (Loc.Language == "ar" ? "اكتمل تحميل قائمة التشغيل" : "Playlist download finished")
            : (Loc.Language == "ar" ? "اكتمل التنزيل" : "Download completed");
        string message = item.IsPlaylist
            ? (Loc.Language == "ar"
                ? $"اكتمل {item.PlaylistCompletedCount} من {item.PlaylistCount} فيديو. فشل {item.PlaylistFailedCount}."
                : $"{item.PlaylistCompletedCount} of {item.PlaylistCount} videos completed. {item.PlaylistFailedCount} failed.")
            : item.Title;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (settings.PlaySoundNotification)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }

            if (settings.ShowDownloadCompletedNotification)
                WindowsToastNotifications.Show(title, message);
        }));
    }

    void Manager_AllDownloadsFinished()
    {
        if (!SettingsStore.Current.SleepWhenDownloadsFinish) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Give the completion toast time to be seen before Windows enters Sleep.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (SettingsStore.Current.SleepWhenDownloadsFinish && _manager.AreAllDownloadsFinished)
                    SetSuspendState(false, false, false);
            };
            timer.Start();
        }));
    }

    [DllImport("PowrProf.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    void NavAdd_Click(object s, RoutedEventArgs e) => ShowAdd();
    void NavDownloads_Click(object s, RoutedEventArgs e) => ShowDownloads();
    void NavHistory_Click(object s, RoutedEventArgs e) => ShowHistory();
    void NavSettings_Click(object s, RoutedEventArgs e) => ShowSettings();
    void NavShortcuts_Click(object s, RoutedEventArgs e) => ShowShortcuts();
    
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        // Ctrl+Tab / Ctrl+Shift+Tab: cycle through the main pages.
        if (e.Key == Key.Tab && modifiers.HasFlag(ModifierKeys.Control)
            && (modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
        {
            e.Handled = true;
            bool reverse = modifiers.HasFlag(ModifierKeys.Shift);
            var pages = new Action[] { ShowAdd, ShowDownloads, ShowHistory, ShowSettings, ShowShortcuts };
            int current = NavAdd.IsChecked == true ? 0
                : NavDownloads.IsChecked == true ? 1
                : NavHistory.IsChecked == true ? 2
                : NavSettings.IsChecked == true ? 3 : 4;
            int next = (current + (reverse ? pages.Length - 1 : 1)) % pages.Length;
            pages[next]();
            return;
        }

        if (CloseConfirmOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseConfirmCancel_Click(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }

        bool control = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        bool typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            || Keyboard.FocusedElement is System.Windows.Controls.PasswordBox;

        if (e.Key == Key.F1 && modifiers == ModifierKeys.None)
        {
            ShowShortcuts();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && modifiers == ModifierKeys.None)
        {
            if (SearchInput.IsKeyboardFocusWithin) { SearchInput.Clear(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (_addView.IsKeyboardFocusWithin) { _add.CancelCommand.Execute(null); e.Handled = true; }
            return;
        }

        if (e.Key == Key.F5 && modifiers == ModifierKeys.None)
        {
            if (NavDownloads.IsChecked == true) ShowDownloads();
            else if (NavHistory.IsChecked == true) ShowHistory();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.L)
        {
            ShowAdd();
            if (_addView.FindName("UrlBox") is System.Windows.Controls.TextBox urlBox) { urlBox.Focus(); urlBox.SelectAll(); }
            e.Handled = true; return;
        }
        if (control && e.Key == Key.O && !typing) { _downloads.OpenSelectedFolder(); e.Handled = true; return; }
        if (control && e.Key == Key.C && !typing) { _downloads.CopySelectedUrl(); e.Handled = true; return; }
        if (control && e.Key == Key.S) { SettingsStore.Save(); e.Handled = true; return; }
        if (control && e.Key == Key.Up) { _add.ChangeSelectedQuality(-1); e.Handled = true; return; }
        if (control && e.Key == Key.Down) { _add.ChangeSelectedQuality(1); e.Handled = true; return; }

        if (control && e.Key == Key.A && !typing)
        {
            if (NavAdd.IsChecked == true && _add.IsPlaylistMode) _add.AllSelected = !shift;
            else if (NavDownloads.IsChecked == true) _downloads.AllSelected = !shift;
            e.Handled = true; return;
        }
        if (e.Key == Key.Delete && !typing && modifiers == ModifierKeys.Control)
        { _downloads.DeleteSelectedCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Delete && !typing && modifiers == ModifierKeys.None)
        { _downloads.RemoveSelectedCommand.Execute(null); e.Handled = true; return; }

        if (control && e.Key == Key.Enter && NavAdd.IsChecked == true)
        { _add.DownloadNowCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Enter && modifiers == ModifierKeys.None && NavAdd.IsChecked == true && !typing)
        {
            if (_add.HasContent) _add.DownloadNowCommand.Execute(null);
            else _add.GetVideoCommand.Execute(null);
            e.Handled = true; return;
        }
        if (e.Key == Key.Enter && modifiers == ModifierKeys.None && !typing && NavDownloads.IsChecked == true)
        { _downloads.OpenSelectedFile(); e.Handled = true; return; }

        if (!control) return;
        switch (e.Key)
        {
            case Key.D1: case Key.NumPad1: ShowAdd(); e.Handled = true; break;
            case Key.D2: case Key.NumPad2: ShowDownloads(); e.Handled = true; break;
            case Key.D3: case Key.NumPad3: ShowHistory(); e.Handled = true; break;
            case Key.D4: case Key.NumPad4: ShowSettings(); e.Handled = true; break;
            case Key.D5: case Key.NumPad5: ShowShortcuts(); e.Handled = true; break;
            case Key.P: _downloads.PauseAllCommand.Execute(null); e.Handled = true; break;
            case Key.R: _downloads.ResumeAllCommand.Execute(null); e.Handled = true; break;
            case Key.F: ShowDownloads(); SearchInput.Focus(); SearchInput.SelectAll(); e.Handled = true; break;
            case Key.OemComma: ShowSettings(); e.Handled = true; break;
        }
    }

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
