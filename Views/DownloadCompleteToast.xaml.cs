using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using El3Almey.ViewModels;

namespace El3Almey.Wpf.Views;

public partial class DownloadCompleteToast : Window
{
    readonly DownloadItem _item;
    readonly DispatcherTimer _timer;

    public DownloadCompleteToast(DownloadItem item, bool arabic)
    {
        InitializeComponent();
        _item = item;
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        HeadingText.Text = arabic ? "اكتمل التنزيل بنجاح" : "Download completed";
        OpenFileButton.Content = arabic ? "فتح الملف" : "Open file";
        OpenFolderButton.Content = arabic ? "فتح مجلد التنزيل" : "Open download folder";
        FileTitleText.Text = string.IsNullOrWhiteSpace(item.Title) ? Path.GetFileName(item.FilePath) : item.Title;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 18;
            Top = area.Bottom - ActualHeight - 18;
        };
        SystemSounds.Asterisk.Play();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        _timer.Start();
    }

    void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(_item.FilePath)) Process.Start(new ProcessStartInfo(_item.FilePath) { UseShellExecute = true }); }
        catch { }
        Close();
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = File.Exists(_item.FilePath) ? _item.FilePath : _item.OutputFolder;
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosed(EventArgs e) { _timer.Stop(); base.OnClosed(e); }
}