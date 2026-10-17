using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class PlaylistLinkDialog : Window
{
    public bool DownloadPlaylist { get; private set; }

    public PlaylistLinkDialog(string? playlistTitle, bool arabic)
    {
        InitializeComponent();
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        HeadingText.Text = arabic ? "فيديو أم قائمة تشغيل؟" : "Video or playlist?";
        SubheadingText.Text = arabic
            ? "الرابط يحتوي على فيديو مرتبط بقائمة تشغيل."
            : "This link contains a video that belongs to a playlist.";
        PlaylistTitleText.Text = string.IsNullOrWhiteSpace(playlistTitle)
            ? (arabic ? "اختر ما تريد تنزيله" : "Choose what you want to download")
            : playlistTitle;
        PlaylistButtonText.Text = arabic ? "تنزيل قائمة التشغيل بالكامل" : "Download full playlist";
        VideoButtonText.Text = arabic ? "تنزيل هذا الفيديو فقط" : "Download only this video";
        CancelButtonText.Text = arabic ? "إلغاء" : "Cancel";
        Title = arabic ? "فيديو أم قائمة تشغيل؟" : "Video or playlist?";
    }

    void PlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadPlaylist = true;
        DialogResult = true;
    }

    void VideoButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadPlaylist = false;
        DialogResult = false;
    }

    void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}