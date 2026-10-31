using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class ChaptersPromptDialog : Window
{
    public bool KeepChapters { get; private set; }

    public ChaptersPromptDialog(string title, bool arabic)
    {
        InitializeComponent();
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = arabic ? "فصول الفيديو" : "Video chapters";
        HeadingText.Text = arabic ? "الفيديو يحتوي على فصول" : "This video has chapters";
        SubheadingText.Text = arabic ? "هل تريد الاحتفاظ بالفصول داخل ملف الفيديو؟" : "Would you like to keep chapters inside the video file?";
        VideoTitleText.Text = title;
        KeepChaptersButtonText.Text = arabic ? "نعم، الاحتفاظ بالفصول" : "Yes, keep chapters";
        WithoutChaptersButtonText.Text = arabic ? "لا، تنزيل بدون فصول" : "No, download without chapters";
        CancelButtonText.Text = arabic ? "إلغاء" : "Cancel";
    }

    private void KeepChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        KeepChapters = true;
        DialogResult = true;
    }

    private void WithoutChaptersButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
