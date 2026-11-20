using System.Windows;

namespace El3Almey.Wpf.Views;

public partial class ThemedConfirmDialog : Window
{
    public ThemedConfirmDialog(string title, string message, string confirmText = "Delete")
    {
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}