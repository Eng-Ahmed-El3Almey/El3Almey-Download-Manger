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
            popup.PlacementTarget = null;
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

    void ActionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DownloadItem item)
        {
            // Let the Button execute its bound command before closing the popup.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => item.IsMenuOpen = false));
        }
    }

    void PlaylistRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement element || element.DataContext is not DownloadItem item || !item.IsPlaylist) return;
        if (DataContext is DownloadsViewModel vm) vm.OpenPlaylist(item);
        e.Handled = true;
    }
}
