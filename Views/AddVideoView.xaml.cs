using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace El3Almey.Wpf.Views;

public partial class AddVideoView : UserControl
{
    public AddVideoView() => InitializeComponent();

    private void UrlBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box) return;
        try
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                box.Text = text.Trim();
                box.CaretIndex = box.Text.Length;
                box.SelectAll();
                e.Handled = true;
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard may be temporarily unavailable when another application owns it.
        }
    }
}
