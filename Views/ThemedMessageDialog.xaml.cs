using System.Windows;
using System.Windows.Media;

namespace El3Almey.Wpf.Views;

public enum ThemedDialogKind { Information, Warning, Error }
public enum ThemedDialogButtons { Ok, YesNo }

public partial class ThemedMessageDialog : Window
{
    public ThemedMessageDialog(string title, string message, ThemedDialogKind kind, ThemedDialogButtons buttons)
    {
        InitializeComponent();
        DialogTitle.Text = title;
        DialogMessage.Text = message;

        switch (kind)
        {
            case ThemedDialogKind.Warning:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 92));
                IconSurface.Background = new SolidColorBrush(Color.FromRgb(67, 48, 27));
                break;
            case ThemedDialogKind.Error:
                IconText.Text = "×";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 130, 152));
                IconSurface.Background = new SolidColorBrush(Color.FromRgb(71, 35, 51));
                break;
            default:
                IconText.Text = "i";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(102, 177, 255));
                IconSurface.Background = new SolidColorBrush(Color.FromRgb(25, 54, 92));
                break;
        }

        if (buttons == ThemedDialogButtons.YesNo)
        {
            NoButton.Visibility = Visibility.Visible;
            YesButton.Content = "Yes";
            NoButton.Content = "No";
        }
        else
        {
            YesButton.Content = "OK";
        }
    }

    public static bool? Show(Window? owner, string title, string message,
        ThemedDialogKind kind = ThemedDialogKind.Information,
        ThemedDialogButtons buttons = ThemedDialogButtons.Ok)
    {
        var dialog = new ThemedMessageDialog(title, message, kind, buttons);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void No_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}