using System;
using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class PlaylistUrlDialog : Window
{
    public string PlaylistUrl => UrlBox.Text.Trim();

    public PlaylistUrlDialog(string playlistTitle)
    {
        InitializeComponent();
        PlaylistTitleText.Text = playlistTitle;
        Loaded += (_, _) => UrlBox.Focus();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(PlaylistUrl, UriKind.Absolute, out var uri)
            || (!uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
                && !uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Enter a valid YouTube playlist URL.", "Invalid playlist URL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}