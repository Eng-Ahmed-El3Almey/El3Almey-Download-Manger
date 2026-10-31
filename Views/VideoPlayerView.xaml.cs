using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace El3Almey.Wpf.Views;

public partial class VideoPlayerView : UserControl
{
    private readonly List<string> _libraryFolders = new();

    public VideoPlayerView()
    {
        InitializeComponent();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video",
            Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v;*.mpeg;*.mpg;*.ts|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        NowPlayingTitle.Text = System.IO.Path.GetFileName(dialog.FileName);
        NowPlayingSource.Text = dialog.FileName;
        MessageBox.Show(
            Window.GetWindow(this),
            "The video has been selected. The playback engine will be connected in the next implementation stage.",
            "Video selected",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Play from URL",
            Width = 520,
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
            Text = "Paste a direct video URL or supported stream link",
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
        var open = new Button { Content = "Continue", Width = 100, Height = 32 };
        open.Click += (_, _) =>
        {
            if (!Uri.TryCreate(urlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(dialog, "Enter a valid HTTP or HTTPS URL.", "Invalid URL",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NowPlayingTitle.Text = uri.Host;
            NowPlayingSource.Text = uri.ToString();
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();

        if (dialog.DialogResult == true)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "The URL has been added as the selected source. Online playback will be connected in the playback-engine stage.",
                "URL selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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