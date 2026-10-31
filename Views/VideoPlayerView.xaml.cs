using LibVLCSharp.Shared;
using El3Almey.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace El3Almey.Wpf.Views;

public partial class VideoPlayerView : UserControl
{
    private readonly VideoLibraryService _library = new();
    private List<string> _visibleLibraryVideos = new();
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _positionTimer;
    private bool _isSeeking;
    private bool _engineReady;

    public VideoPlayerView()
    {
        InitializeComponent();

        Core.Initialize();
        _libVLC = new LibVLC("--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVLC);
        VideoSurface.MediaPlayer = _mediaPlayer;

        _mediaPlayer.Playing += (_, _) => Dispatcher.Invoke(() =>
        {
            EmptyPlayerPanel.Visibility = Visibility.Collapsed;
            PlayPauseButton.Content = "Ⅱ  Pause";
            PositionSlider.IsEnabled = true;
        });
        _mediaPlayer.Paused += (_, _) => Dispatcher.Invoke(() => PlayPauseButton.Content = "▶  Play");
        _mediaPlayer.Stopped += (_, _) => Dispatcher.Invoke(() => PlayPauseButton.Content = "▶  Play");
        _mediaPlayer.EncounteredError += (_, _) => Dispatcher.Invoke(() =>
            MessageBox.Show(Window.GetWindow(this),
                "The media could not be played. Check the URL or file format and try again.",
                "Playback error", MessageBoxButton.OK, MessageBoxImage.Error));

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _positionTimer.Start();
        _engineReady = true;
        VolumeSlider.Value = 80;
        RefreshLibrary();
    }

    private void PlayLocalFile(string filePath)
    {
        if (!_engineReady || !File.Exists(filePath))
            return;

        try
        {
            using var media = new Media(_libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Play(media);
            NowPlayingTitle.Text = Path.GetFileName(filePath);
            NowPlayingSource.Text = filePath;
            EmptyPlayerPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex.Message);
        }
    }

    private void PlayNetworkUrl(string value)
    {
        if (!_engineReady || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a valid direct HTTP or HTTPS media URL.",
                "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var media = new Media(_libVLC, uri.ToString(), FromType.FromLocation);
            _mediaPlayer.Play(media);
            NowPlayingTitle.Text = uri.Host;
            NowPlayingSource.Text = uri.ToString();
            EmptyPlayerPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex.Message);
        }
    }

    private void ShowPlaybackError(string details)
    {
        MessageBox.Show(Window.GetWindow(this), details, "Playback error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video",
            Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpeg;*.mpg;*.ts;*.flv;*.3gp|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            PlayLocalFile(dialog.FileName);
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Play from URL",
            Width = 540,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["CardBg"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextMain"]
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste a direct video or network stream URL (HTTP / HTTPS)",
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 13
        });

        var urlBox = new TextBox
        {
            Height = 34,
            Padding = new Thickness(9, 5, 9, 5),
            FontSize = 13,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["InputBg"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextMain"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["CardBorder"],
            CaretBrush = System.Windows.Media.Brushes.White
        };
        panel.Children.Add(urlBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var open = new Button { Content = "Play", Width = 100, Height = 32 };
        open.Click += (_, _) =>
        {
            if (!Uri.TryCreate(urlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(dialog, "Enter a valid HTTP or HTTPS URL.", "Invalid URL",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (dialog.ShowDialog() == true)
            PlayNetworkUrl(urlBox.Text);
    }


    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_engineReady || _mediaPlayer.Media == null)
            return;

        var appWindow = Window.GetWindow(this);
        if (appWindow?.Content is not Grid appRoot || VideoSurface.Parent is not Grid originalVideoHost)
            return;

        // Reuse the SAME VideoView and MediaPlayer. Creating a second window / VideoView
        // can detach LibVLC's video output and leave the new surface black.
        var previousWindowState = appWindow.WindowState;
        var previousWindowStyle = appWindow.WindowStyle;
        var previousResizeMode = appWindow.ResizeMode;
        var originalIndex = originalVideoHost.Children.IndexOf(VideoSurface);
        originalVideoHost.Children.Remove(VideoSurface);

        var accent = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 132, 255));
        var panel = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 18, 35));
        var surface = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(3, 7, 15));
        var line = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 57, 91));
        var muted = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 170, 203));

        var overlay = new Grid { Background = surface, Focusable = true };
        overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        VideoSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
        VideoSurface.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(VideoSurface, 0);
        overlay.Children.Add(VideoSurface);

        Button MakeButton(string text, double width = 76, bool primary = false)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 36,
                Margin = new Thickness(4, 0, 4, 0),
                Background = primary ? accent : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 34, 58)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = primary ? accent : line,
                BorderThickness = new Thickness(1),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        var controls = new Border
        {
            Background = panel,
            BorderBrush = line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 14, 24, 16)
        };
        Grid.SetRow(controls, 1);
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var exit = MakeButton("Exit Full Screen  ×", 148, true);
        exit.Click += (_, _) => ExitFullScreen();
        DockPanel.SetDock(exit, Dock.Right);
        titleBar.Children.Add(exit);
        titleBar.Children.Add(new TextBlock
        {
            Text = NowPlayingTitle.Text,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        content.Children.Add(titleBar);

        var seekRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var current = new TextBlock { Text = FormatTime(_mediaPlayer.Time), Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        seekRow.Children.Add(current);
        var seek = new Slider { Minimum = 0, Maximum = 1000, IsEnabled = _mediaPlayer.Length > 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Foreground = accent, Height = 24 };
        Grid.SetColumn(seek, 1);
        seekRow.Children.Add(seek);
        var duration = new TextBlock { Text = FormatTime(_mediaPlayer.Length), Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(duration, 2);
        seekRow.Children.Add(duration);
        bool seeking = false;
        seek.PreviewMouseLeftButtonDown += (_, _) => seeking = true;
        seek.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_mediaPlayer.Length > 0)
                _mediaPlayer.Time = (long)(seek.Value / 1000d * _mediaPlayer.Length);
            seeking = false;
        };
        content.Children.Add(seekRow);
        Grid.SetRow(seekRow, 1);

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var center = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(MakeButton("−10 sec"));
        ((Button)center.Children[0]).Click += (_, _) => SeekTo(Math.Max(0, _mediaPlayer.Time - 10_000));
        var play = MakeButton(_mediaPlayer.IsPlaying ? "Pause" : "Play", 86, true);
        play.Click += (_, _) => { if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause(); else _mediaPlayer.Play(); };
        center.Children.Add(play);
        var forward = MakeButton("+10 sec");
        forward.Click += (_, _) => SeekTo(Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 10_000));
        center.Children.Add(forward);
        actions.Children.Add(center);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "Speed", Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var speed = new ComboBox { Width = 82, Height = 32, SelectedIndex = Math.Max(0, SpeedCombo.SelectedIndex), Margin = new Thickness(0, 0, 20, 0) };
        foreach (var rate in new[] { "0.5x", "0.75x", "1.0x", "1.25x", "1.5x", "2.0x" })
            speed.Items.Add(new ComboBoxItem { Content = rate });
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedItem is ComboBoxItem item &&
                float.TryParse(item.Content?.ToString()?.Replace("x", ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rate))
            {
                _mediaPlayer.SetRate(rate);
                SpeedCombo.SelectedIndex = speed.SelectedIndex;
            }
        };
        right.Children.Add(speed);
        var mute = MakeButton(_mediaPlayer.Mute ? "Unmute" : "Mute", 70);
        mute.Click += (_, _) => { _mediaPlayer.Mute = !_mediaPlayer.Mute; mute.Content = _mediaPlayer.Mute ? "Unmute" : "Mute"; };
        right.Children.Add(mute);
        var volume = new Slider { Width = 124, Minimum = 0, Maximum = 100, Value = _mediaPlayer.Volume, VerticalAlignment = VerticalAlignment.Center, Foreground = accent };
        volume.ValueChanged += (_, args) => _mediaPlayer.Volume = (int)args.NewValue;
        right.Children.Add(volume);
        Grid.SetColumn(right, 1);
        actions.Children.Add(right);
        content.Children.Add(actions);
        Grid.SetRow(actions, 2);
        controls.Child = content;
        overlay.Children.Add(controls);

        appRoot.Children.Add(overlay);
        Panel.SetZIndex(overlay, 9999);
        appWindow.WindowStyle = WindowStyle.None;
        appWindow.ResizeMode = ResizeMode.NoResize;
        appWindow.WindowState = WindowState.Maximized;
        overlay.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { ExitFullScreen(); args.Handled = true; }
            else if (args.Key == Key.Space)
            {
                if (_mediaPlayer.IsPlaying) _mediaPlayer.Pause(); else _mediaPlayer.Play();
                args.Handled = true;
            }
            else if (args.Key == Key.Left) { SeekTo(Math.Max(0, _mediaPlayer.Time - 10_000)); args.Handled = true; }
            else if (args.Key == Key.Right) { SeekTo(Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 10_000)); args.Handled = true; }
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (!seeking && _mediaPlayer.Length > 0)
                seek.Value = Math.Clamp((double)_mediaPlayer.Time / _mediaPlayer.Length * 1000, 0, 1000);
            current.Text = FormatTime(_mediaPlayer.Time);
            duration.Text = FormatTime(_mediaPlayer.Length);
            play.Content = _mediaPlayer.IsPlaying ? "Pause" : "Play";
        };
        overlay.Unloaded += (_, _) => timer.Stop();

        void ExitFullScreen()
        {
            if (!appRoot.Children.Contains(overlay))
                return;

            timer.Stop();
            overlay.Children.Remove(VideoSurface);
            appRoot.Children.Remove(overlay);
            Grid.SetRow(VideoSurface, 0);
            if (originalIndex >= 0 && originalIndex <= originalVideoHost.Children.Count)
                originalVideoHost.Children.Insert(originalIndex, VideoSurface);
            else
                originalVideoHost.Children.Add(VideoSurface);
            VideoSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
            VideoSurface.VerticalAlignment = VerticalAlignment.Stretch;
            appWindow.WindowStyle = previousWindowStyle;
            appWindow.ResizeMode = previousResizeMode;
            appWindow.WindowState = previousWindowState;
            Keyboard.ClearFocus();
        }

        timer.Start();
        overlay.Focus();
        appWindow.Activate();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.Media == null)
        {
            OpenFile_Click(sender, e);
            return;
        }

        if (_mediaPlayer.IsPlaying)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    private void Back10_Click(object sender, RoutedEventArgs e) =>
        SeekTo(Math.Max(0, _mediaPlayer.Time - 10_000));

    private void Forward10_Click(object sender, RoutedEventArgs e) =>
        SeekTo(Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + 10_000));

    private void SeekTo(long milliseconds)
    {
        if (_mediaPlayer.Length > 0)
            _mediaPlayer.Time = Math.Clamp(milliseconds, 0, _mediaPlayer.Length);
    }

    private void UpdatePlaybackPosition()
    {
        if (!_mediaPlayer.IsPlaying && _mediaPlayer.State != VLCState.Paused)
            return;

        long current = Math.Max(0, _mediaPlayer.Time);
        long duration = Math.Max(0, _mediaPlayer.Length);
        CurrentTimeText.Text = FormatTime(current);
        DurationText.Text = FormatTime(duration);

        if (!_isSeeking && duration > 0)
            PositionSlider.Value = Math.Clamp((double)current / duration * 1000, 0, 1000);
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _isSeeking = true;

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_mediaPlayer.Length > 0)
            SeekTo((long)(PositionSlider.Value / 1000 * _mediaPlayer.Length));
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSeeking && _mediaPlayer.Length > 0)
            CurrentTimeText.Text = FormatTime((long)(PositionSlider.Value / 1000 * _mediaPlayer.Length));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeValueText != null)
            VolumeValueText.Text = $"{(int)e.NewValue}%";

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = (int)e.NewValue;
            if (e.NewValue > 0 && _mediaPlayer.Mute)
                _mediaPlayer.Mute = false;
            if (MuteButton != null)
                MuteButton.Content = _mediaPlayer.Mute ? "\uE74F" : "\uE767";
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer.Mute = !_mediaPlayer.Mute;
        MuteButton.Content = _mediaPlayer.Mute ? "\uE74F" : "\uE767";
        MuteButton.ToolTip = _mediaPlayer.Mute ? "Unmute" : "Mute";
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mediaPlayer == null || SpeedCombo.SelectedItem is not ComboBoxItem item ||
            !float.TryParse(item.Content?.ToString()?.Replace("x", ""),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float rate))
            return;

        _mediaPlayer.SetRate(rate);
    }

    private void OpenSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load subtitle",
            Filter = "Subtitle files|*.srt;*.ass;*.ssa;*.vtt|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true || _mediaPlayer.Media == null)
            return;

        if (!_mediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(dialog.FileName).AbsoluteUri, true))
            MessageBox.Show(Window.GetWindow(this), "The subtitle could not be loaded.",
                "Subtitle", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Add a video library folder",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        if (_library.AddFolder(dialog.FolderName))
            RefreshLibrary();
        else
            MessageBox.Show(Window.GetWindow(this),
                "This folder is already in the library or cannot be accessed.",
                "Video Library", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshLibrary_Click(object sender, RoutedEventArgs e) => RefreshLibrary();

    private void ToggleLibrary_Click(object sender, RoutedEventArgs e)
    {
        bool hide = LibraryPanel.Visibility == Visibility.Visible;
        LibraryPanel.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        LibraryColumn.Width = hide ? new GridLength(0) : new GridLength(330);
        ToggleLibraryButton.Content = hide ? "Show Library" : "Hide Library";
    }

    private void RefreshLibrary()
    {
        _visibleLibraryVideos = _library.GetVideos().ToList();
        ApplyLibraryFilter();
    }

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyLibraryFilter();

    private void ApplyLibraryFilter()
    {
        if (LibraryList == null)
            return;

        string query = LibrarySearchBox.Text?.Trim() ?? string.Empty;
        var videos = _visibleLibraryVideos
            .Where(path => string.IsNullOrWhiteSpace(query) ||
                Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        LibraryList.Items.Clear();
        foreach (string path in videos)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(path),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextMain"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            content.Children.Add(new TextBlock
            {
                Text = path,
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextMuted"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            LibraryList.Items.Add(new ListBoxItem { Content = content, Tag = path });
        }

        if (LibraryList.Items.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No videos found. Add a folder or refresh the library.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextMuted"],
                Margin = new Thickness(8, 16, 8, 8)
            };
            LibraryList.Items.Add(new ListBoxItem { Content = empty, IsHitTestVisible = false });
        }
    }

    private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LibraryList.SelectedItem is ListBoxItem { Tag: string path } && File.Exists(path))
            PlayLocalFile(path);
    }
}