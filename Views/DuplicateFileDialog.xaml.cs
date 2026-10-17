using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class DuplicateFileDialog : Window
{
    public bool DownloadWithNewName { get; private set; }

    public DuplicateFileDialog(string title, bool arabic)
    {
        InitializeComponent();
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = arabic ? "الملف موجود بالفعل" : "Existing download";
        HeadingText.Text = arabic ? "الفيديو موجود بالفعل" : "Video already exists";
        SubheadingText.Text = arabic ? "اختار الإجراء المناسب" : "Choose what you want to do";
        VideoTitleText.Text = title;
        NewNameButtonText.Text = arabic ? "تحميل باسم جديد" : "Download with a new name";
        SkipButtonText.Text = arabic ? "تخطي هذا الفيديو" : "Skip this video";
        CancelButtonText.Text = arabic ? "إلغاء" : "Cancel";
    }

    private void NewNameButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadWithNewName = true;
        DialogResult = true;
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
