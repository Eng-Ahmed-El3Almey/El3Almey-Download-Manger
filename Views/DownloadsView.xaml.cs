using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using El3Almey.ViewModels;

namespace El3Almey.Wpf.Views;
public partial class DownloadsView : UserControl
{
    public DownloadsView()
    {
        InitializeComponent();
        Loaded += View_Loaded;
        Unloaded += View_Unloaded;
    }

    Window? _hostWindow;

    void View_Loaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hostWindow)) return;
        if (_hostWindow != null) _hostWindow.Deactivated -= HostWindow_Deactivated;
        _hostWindow = window;
        if (_hostWindow != null) _hostWindow.Deactivated += HostWindow_Deactivated;
    }

    void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow != null) _hostWindow.Deactivated -= HostWindow_Deactivated;
        _hostWindow = null;
        CloseAllMenus();
    }

    void HostWindow_Deactivated(object? sender, EventArgs e) => CloseAllMenus();

    void UserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            // Do not close the Popup before its Button.Click handler can execute.
            if (current is Button menuButton && IsMenuAction(menuButton.Tag?.ToString())) return;

            if (current is ToggleButton toggle)
            {
                CloseAllMenusExcept(toggle.DataContext as DownloadItem);
                return;
            }

            DependencyObject? parent = null;
            try { parent = VisualTreeHelper.GetParent(current); } catch { }
            current = parent ?? LogicalTreeHelper.GetParent(current);
        }

        CloseAllMenus();
    }

    void CloseAllMenus() => CloseAllMenusExcept(null);

    void CloseAllMenusExcept(DownloadItem? keepItem)
    {
        if (DataContext is DownloadsViewModel vm)
        {
            foreach (var item in vm.Items) if (!ReferenceEquals(item, keepItem)) item.IsMenuOpen = false;
            foreach (var item in vm.PlaylistItems) if (!ReferenceEquals(item, keepItem)) item.IsMenuOpen = false;
        }
    }

    void DownloadRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement row || row.DataContext is not DownloadItem item) return;
        var popup = FindChildPopup(row);
        if (popup != null)
        {
            popup.PlacementTarget = row;
            popup.Placement = PlacementMode.MousePoint;
        }
        item.IsMenuOpen = true;
        e.Handled = true;
    }

    void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button || button.DataContext is not DownloadItem item) return;
        var popup = FindChildPopup(button);
        if (popup != null)
        {
            popup.PlacementTarget = button;
            popup.Placement = PlacementMode.Bottom;
        }
        // ToggleButton binding handles opening/closing; do not force IsMenuOpen here.
    }

    static Popup? FindChildPopup(DependencyObject start)
    {
        for (DependencyObject? current = start; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Popup popup) return popup;
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var found = FindPopupRecursive(VisualTreeHelper.GetChild(current, i));
                if (found != null) return found;
            }
        }
        return null;
    }

    static Popup? FindPopupRecursive(DependencyObject current)
    {
        if (current is Popup popup) return popup;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(current); } catch { return null; }
        for (int i = 0; i < count; i++)
        {
            var found = FindPopupRecursive(VisualTreeHelper.GetChild(current, i));
            if (found != null) return found;
        }
        return null;
    }

    static bool IsMenuAction(string? tag) => tag is "OpenFile" or "OpenFolder" or "CopyTitle" or "CopyUrl" or "Retry" or "AddToQueue" or "RemoveFromHistory" or "AskDelete";

    void ActionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement element || DataContext is not DownloadsViewModel vm) return;

        // WPF Popup content is hosted in a separate presentation source. Resolve the
        // target item from the popup binding first, then fall back to the one open menu.
        var item = element.DataContext as DownloadItem
            ?? vm.Items.FirstOrDefault(i => i.IsMenuOpen)
            ?? vm.PlaylistItems.FirstOrDefault(i => i.IsMenuOpen);
        if (item == null) return;

        switch (element.Tag?.ToString())
        {
            case "OpenFile": vm.OpenFileCommand.Execute(item); break;
            case "OpenFolder": vm.OpenFolderCommand.Execute(item); break;
            case "CopyTitle": vm.CopyTitleCommand.Execute(item); break;
            case "CopyUrl": vm.CopyUrlCommand.Execute(item); break;
            case "Retry": vm.RetryCommand.Execute(item); break;
            case "AddToQueue": vm.AddToQueueCommand.Execute(item); break;
            case "RemoveFromHistory": vm.RemoveFromHistoryCommand.Execute(item); break;
            case "AskDelete": vm.AskDeleteCommand.Execute(item); break;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => item.IsMenuOpen = false));
    }

    void PlaylistRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement element || element.DataContext is not DownloadItem item || !item.IsPlaylist) return;
        if (DataContext is DownloadsViewModel vm) vm.OpenPlaylist(item);
        e.Handled = true;
    }
}
