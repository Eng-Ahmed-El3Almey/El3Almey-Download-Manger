using LibVLCSharp.Shared;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace El3Almey.Wpf.Views;

public partial class VideoPlayerView : UserControl
{
    private readonly List<string> _libraryFolders = new();
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
            PlayPauseButton.Content = "\uE769";
            PositionSlider.IsEnabled = true;
        });
        _mediaPlayer.Paused += (_, _) => Dispatcher.Invoke(() => PlayPauseButton.Content = "\uE768");
        _mediaPlayer.Stopped += (_, _) => Dispatcher.Invoke(() => PlayPauseButton.Content = "\uE768");
        _mediaPlayer.EncounteredError += (_, _) => Dispatcher.Invoke(() =>
            MessageBox.Show(Window.GetWindow(this),
                "The media could not be played. Check the URL or file format and try again.",
                "Playback error", MessageBoxButton.OK, MessageBoxImage.Error));

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _positionTimer.Start();
        _engineReady = true;
        VolumeSlider.Value = 80;
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
        if (_mediaPlayer != null)
            _mediaPlayer.Volume = (int)e.NewValue;
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

        if (!_libraryFolders.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
            _libraryFolders.Add(dialog.FolderName);

        FolderSummary.Text = string.Join(Environment.NewLine, _libraryFolders);
    }
}