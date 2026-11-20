using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using El3Almey.Services;

namespace El3Almey.Wpf.Views;

public partial class ErrorLogView : UserControl
{
    private readonly ObservableCollection<ErrorLogEntry> _allEntries = new();
    private string? _selectedLogPath;
    private string _activeFilter = "All";

    public ErrorLogView()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _allEntries;
        Refresh();
    }

    public void Refresh()
    {
        _allEntries.Clear();
        var logDirectory = Path.Combine(SettingsStore.DataDir, "logs");
        var candidates = new[]
        {
            Path.Combine(logDirectory, "download-errors.log"),
            Path.Combine(SettingsStore.DataDir, "crash.log"),
            Path.Combine(SettingsStore.DataDir, "download-manager.log"),
            Path.Combine(SettingsStore.DataDir, "tool-preparation.log")
        };

        int fileCount = 0;
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            fileCount++;
            try
            {
                var text = File.ReadAllText(path);
                foreach (var block in SplitEntries(text))
                {
                    if (string.IsNullOrWhiteSpace(block)) continue;
                    _allEntries.Add(CreateEntry(path, block));
                }
            }
            catch (Exception ex)
            {
                _allEntries.Add(new ErrorLogEntry
                {
                    SourceName = Path.GetFileName(path),
                    Title = "Could not read log file",
                    Summary = ex.Message,
                    Details = ex.ToString(),
                    LogPath = path,
                    TimeText = "Read error"
                });
            }
        }

        CountText.Text = _allEntries.Count.ToString();
        DownloadCountText.Text = _allEntries.Count(e => e.SourceName.Equals("download-errors.log", StringComparison.OrdinalIgnoreCase)).ToString();
        CrashCountText.Text = _allEntries.Count(e => e.SourceName.Equals("crash.log", StringComparison.OrdinalIgnoreCase)).ToString();
        ApplyFilter();
        if (EntriesList.Items.Count > 0) EntriesList.SelectedIndex = 0;
        else ShowEmpty();
    }

    private static IEnumerable<string> SplitEntries(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (normalized.Contains("\n---\n", StringComparison.Ordinal))
            return normalized.Split("\n---\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Crash and tool logs may not use a delimiter. Split on timestamp headers while
        // preserving stack traces belonging to each event.
        var lines = normalized.Split('\n');
        var blocks = new List<string>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("[", StringComparison.Ordinal) && current.Count > 0)
            {
                blocks.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(string.Join(Environment.NewLine, current));
        return blocks;
    }

    private static ErrorLogEntry CreateEntry(string path, string block)
    {
        var firstLine = block.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Log entry";
        var time = "";
        if (firstLine.StartsWith("["))
        {
            var end = firstLine.IndexOf(']');
            if (end > 1) time = firstLine[1..end];
        }
        var title = Path.GetFileName(path) switch
        {
            "download-errors.log" => "Download failed",
            "crash.log" => "Application error",
            "download-manager.log" => "Download manager storage error",
            "tool-preparation.log" => "Tool preparation error",
            _ => "Application event"
        };
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = lines.FirstOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || l.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || l.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || l.Contains("No space left", StringComparison.OrdinalIgnoreCase)) ?? firstLine;
        return new ErrorLogEntry
        {
            SourceName = Path.GetFileName(path),
            Title = title,
            Summary = MakeFriendly(summary),
            Details = block.Trim(),
            LogPath = path,
            TimeText = time.Length > 0 ? time : "Time unavailable"
        };
    }

    private static string MakeFriendly(string text)
    {
        if (text.Contains("Private video", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Sign in", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cookies-from-browser", StringComparison.OrdinalIgnoreCase))
            return "YouTube requires an authenticated session to access this video. Sign in to the account that has permission to watch it, then retry. If it is a private video, your account must be approved by its owner.";
        if (text.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("This video is private", StringComparison.OrdinalIgnoreCase))
            return "This video is private or unavailable to the current YouTube account. Check access permissions before retrying.";
        if (text.Contains("No space left on device", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Errno 28", StringComparison.OrdinalIgnoreCase))
            return "The destination drive is out of free space. Free up storage, then retry the download.";
        if (text.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase))
            return "YouTube is temporarily limiting requests. Wait a while before retrying.";
        if (text.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase))
            return "The source refused the request. The media link or access permissions may have changed.";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "The request timed out. Check your connection and try again.";
        if (text.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            return "Windows denied access to a file or folder. Check its permissions.";
        return text.Length > 260 ? text[..260] + "…" : text;
    }

    private void ApplyFilter()
    {
        var query = FilterBox.Text.Trim();
        var filtered = _allEntries.Where(e =>
            (_activeFilter == "All"
                || (_activeFilter == "Downloads" && e.SourceName.Equals("download-errors.log", StringComparison.OrdinalIgnoreCase))
                || (_activeFilter == "Crashes" && e.SourceName.Equals("crash.log", StringComparison.OrdinalIgnoreCase)))
            && (string.IsNullOrWhiteSpace(query)
                || (e.Title + " " + e.Summary + " " + e.SourceName + " " + e.Details)
                    .Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        EntriesList.ItemsSource = filtered;
        if (filtered.Count == 0)
            ShowEmpty();
    }

    private void ShowEmpty()
    {
        DetailTitle.Text = "No log entries found";
        DetailMeta.Text = "Log files will appear here when the application records an issue.";
        FriendlyReason.Text = "No errors were found in the supported application log files.";
        RawDetails.Text = "";
        SourcePathText.Text = "";
        _selectedLogPath = null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void FilterAll_Click(object sender, RoutedEventArgs e) => SetFilter("All");
    private void FilterDownloads_Click(object sender, RoutedEventArgs e) => SetFilter("Downloads");
    private void FilterCrashes_Click(object sender, RoutedEventArgs e) => SetFilter("Crashes");

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ErrorLogEntry entry)
        {
            MessageBox.Show("Select an error entry first.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            "Delete this log entry? This action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var content = File.ReadAllText(entry.LogPath);
            var blocks = SplitEntries(content).ToList();
            var index = blocks.FindIndex(block => string.Equals(block.Trim(), entry.Details.Trim(), StringComparison.Ordinal));
            if (index < 0)
            {
                MessageBox.Show("The selected entry could not be found in the source log. Refresh the list and try again.",
                    "Delete Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            blocks.RemoveAt(index);
            var delimiter = content.Replace("\r\n", "\n").Contains("\n---\n", StringComparison.Ordinal)
                ? Environment.NewLine + "---" + Environment.NewLine
                : Environment.NewLine;
            File.WriteAllText(entry.LogPath, string.Join(delimiter, blocks));
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the selected entry.\n\n{ex.Message}",
                "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete all entries from the application log files?\n\nThis clears download errors, crash reports and other supported logs. This action cannot be undone.",
            "Confirm Delete All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var logDirectory = Path.Combine(SettingsStore.DataDir, "logs");
        var candidates = new[]
        {
            Path.Combine(logDirectory, "download-errors.log"),
            Path.Combine(SettingsStore.DataDir, "crash.log"),
            Path.Combine(SettingsStore.DataDir, "download-manager.log"),
            Path.Combine(SettingsStore.DataDir, "tool-preparation.log")
        };

        var failures = new List<string>();
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try { File.WriteAllText(path, string.Empty); }
            catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }

        Refresh();
        if (failures.Count > 0)
            MessageBox.Show("Some log files could not be cleared:\n\n" + string.Join("\n", failures),
                "Delete All", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SetFilter(string filter)
    {
        _activeFilter = filter;
        var active = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 91, 148));
        var inactive = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 36, 61));
        FilterAllButton.Background = filter == "All" ? active : inactive;
        FilterDownloadsButton.Background = filter == "Downloads" ? active : inactive;
        FilterCrashesButton.Background = filter == "Crashes" ? active : inactive;
        ApplyFilter();
        if (EntriesList.Items.Count > 0) EntriesList.SelectedIndex = 0;
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ErrorLogEntry entry) { ShowEmpty(); return; }
        DetailTitle.Text = entry.Title;
        DetailMeta.Text = $"{entry.TimeText}  •  {entry.SourceName}";
        FriendlyReason.Text = entry.Summary;
        RawDetails.Text = entry.Details;
        SourcePathText.Text = entry.LogPath;
        _selectedLogPath = entry.LogPath;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ErrorLogEntry entry) return;
        try { Clipboard.SetText(entry.Details); } catch { }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenPath(_selectedLogPath);
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenPath(Path.Combine(SettingsStore.DataDir, "logs"));

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
            {
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    Process.Start(new ProcessStartInfo(parent) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private sealed class ErrorLogEntry
    {
        public string SourceName { get; init; } = "";
        public string Title { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Details { get; init; } = "";
        public string LogPath { get; init; } = "";
        public string TimeText { get; init; } = "";
    }
}