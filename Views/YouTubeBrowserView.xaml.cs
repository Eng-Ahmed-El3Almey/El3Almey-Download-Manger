using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace El3Almey.Wpf.Views;

public partial class YouTubeBrowserView : UserControl
{
    private bool _initialized;
    private string _currentUrl = "https://www.youtube.com/";
    public event Action<string>? DownloadRequested;

    public YouTubeBrowserView()
    {
        InitializeComponent();
        Loaded += BrowserView_Loaded;
    }

    private async void BrowserView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            string profilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "El3Almey", "YouTubeBrowserProfile");
            Directory.CreateDirectory(profilePath);

            var options = new CoreWebView2EnvironmentOptions();
            var environment = await CoreWebView2Environment.CreateAsync(null, profilePath, options);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                _currentUrl = args.Uri;
                Dispatcher.Invoke(UpdateBrowserToolbar);
            };
            Browser.CoreWebView2.SourceChanged += (_, _) =>
                Dispatcher.Invoke(UpdateBrowserToolbar);
            Browser.CoreWebView2.HistoryChanged += (_, _) =>
                Dispatcher.Invoke(UpdateBrowserToolbar);
            Browser.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    UpdateBrowserToolbar();
                });
            };
            Browser.CoreWebView2.Navigate(_currentUrl);
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(Window.GetWindow(this),
                "YouTube Browser could not be initialized. Make sure Microsoft Edge WebView2 Runtime is installed.\n\n" + ex.Message,
                "YouTube Browser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateBrowserToolbar()
    {
        string activeUrl = Browser.Source?.ToString() ?? _currentUrl;
        _currentUrl = activeUrl;
        AddressBox.Text = activeUrl;
        BackButton.IsEnabled = Browser.CoreWebView2?.CanGoBack == true;
        ForwardButton.IsEnabled = Browser.CoreWebView2?.CanGoForward == true;
        DownloadButton.Visibility = IsSupportedYouTubeContent(activeUrl)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsSupportedYouTubeContent(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        string host = uri.Host.ToLowerInvariant();
        bool youtubeHost = host == "youtube.com" || host.EndsWith(".youtube.com")
            || host == "youtu.be";
        if (!youtubeHost) return false;
        string path = uri.AbsolutePath;
        return (host == "youtu.be" && path.Length > 1)
            || path.Equals("/watch", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/live/", StringComparison.OrdinalIgnoreCase)
            || (path.Equals("/playlist", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(uri.Query));
    }

    private void Navigate(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            value = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(value.Trim());
            uri = new Uri(value);
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        _currentUrl = uri.ToString();
        Browser.CoreWebView2?.Navigate(_currentUrl);
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Navigate(AddressBox.Text);
        e.Handled = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoBack == true) Browser.CoreWebView2.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2?.CanGoForward == true) Browser.CoreWebView2.GoForward();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.Reload();

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (IsSupportedYouTubeContent(_currentUrl))
            DownloadRequested?.Invoke(_currentUrl);
    }
}