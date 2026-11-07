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
        FilesText.Text = fileCount.ToString();
        LatestText.Text = _allEntries.Count == 0 ? "—" : _allEntries[^1].TimeText;
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
        var filtered = _allEntries.Where(e => string.IsNullOrWhiteSpace(query)
            || (e.Title + " " + e.Summary + " " + e.SourceName + " " + e.Details)
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        EntriesList.ItemsSource = filtered;
        CountText.Text = filtered.Count.ToString();
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