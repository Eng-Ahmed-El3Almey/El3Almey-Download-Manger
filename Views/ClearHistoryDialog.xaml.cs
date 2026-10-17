using System.Media;
using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class ClearHistoryDialog : Window
{
    public ClearHistoryDialog(string message, bool arabic)
    {
        InitializeComponent();
        SystemSounds.Exclamation.Play();
        FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = arabic ? "مسح سجل التنزيلات" : "Clear history";
        HeadingText.Text = arabic ? "هل تريد مسح سجل التنزيلات؟" : "Clear download history?";
        SubheadingText.Text = arabic ? "سيتم حذف السجل فقط." : "This only removes entries from history.";
        MessageText.Text = arabic
            ? "لن يتم حذف الملفات التي تم تنزيلها من جهازك."
            : "Downloaded files will not be deleted from your computer.";
        CancelButton.Content = arabic ? "إلغاء" : "Cancel";
        ConfirmButton.Content = arabic ? "مسح السجل" : "Clear history";
    }

    void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}