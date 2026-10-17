using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    readonly DownloadManager _mgr;
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public ObservableCollection<DownloadItem> PlaylistItems { get; } = new();
    [ObservableProperty] private bool _isPlaylistDetailOpen;
    [ObservableProperty] private string _playlistDetailTitle = "";
    [ObservableProperty] private string _playlistDetailSummary = "";
    [ObservableProperty] private string _searchText = "";
    public IReadOnlyList<string> FilterOptions { get; } = new[] { "All", "Video", "Audio", "Playlist", "Channel" };
    [ObservableProperty] private string _selectedFilter = "All";

    public Func<string, bool>? Confirm { get; set; }
    public bool IsEmpty => Items.Count == 0;
    public string CountText => $"{Items.Count} {Loc.T("S.Items")}";

    public HistoryViewModel(DownloadManager mgr)
    {
        _mgr = mgr;
        _mgr.Changed += Refresh;
        Loc.Changed += () => { foreach (var i in _mgr.Items) i.RefreshLanguage(); OnPropertyChanged(nameof(CountText)); };
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedFilterChanged(string value) => Refresh();

    public void Refresh()
    {
        var q = _mgr.Items.Where(i => (!i.IsPlaylist || i.IsPlaylistRepresentative) && (i.Status is DownloadStatus.Completed or DownloadStatus.Failed));
        q = SelectedFilter switch
        {
            "Video" => q.Where(i => !i.IsAudio && !i.IsPlaylist && !IsChannelUrl(i.Url)),
            "Audio" => q.Where(i => i.IsAudio && !i.IsPlaylist),
            "Playlist" => q.Where(i => i.IsPlaylistRepresentative),
            "Channel" => q.Where(i => IsChannelUrl(i.Url)),
            _ => q
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            q = q.Where(i => (i.IsPlaylistRepresentative ? i.PlaylistTitle : i.Title).Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        var list = q.OrderByDescending(i => i.FinishedAt ?? i.AddedAt).ToList();
        Items.Clear();
        foreach (var i in list) Items.Add(i);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    static bool IsChannelUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("youtube.com/@", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtube.com/channel/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtube.com/c/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("youtube.com/user/", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand] void Open(DownloadItem i) => FileActions.OpenFile(i.FilePath);

    [RelayCommand]
    void OpenPlaylistDetails(DownloadItem item)
    {
        if (!item.IsPlaylistRepresentative) return;
        var children = _mgr.Items.Where(i => i.IsPlaylist && i.PlaylistGroupId == item.PlaylistGroupId)
            .OrderBy(i => i.PlaylistIndex).ToList();
        PlaylistItems.Clear();
        foreach (var child in children) PlaylistItems.Add(child);
        PlaylistDetailTitle = item.PlaylistTitle;
        long total = children.Sum(i => Math.Max(0, i.TotalBytes));
        PlaylistDetailSummary = $"{children.Count} videos • {children.Count(i => i.Status == DownloadStatus.Completed)} completed • {children.Count(i => i.Status == DownloadStatus.Failed)} failed" +
            (total > 0 ? $" • Total size: {Fmt.Bytes(total)}" : " • Total size unavailable");
        IsPlaylistDetailOpen = true;
    }

    [RelayCommand] void BackFromPlaylist() => IsPlaylistDetailOpen = false;

    [RelayCommand]
    void PlaylistItemAction(DownloadItem item)
    {
        if (item.Status == DownloadStatus.Completed) FileActions.OpenFile(item.FilePath);
        else _mgr.Retry(item);
    }

    [RelayCommand] void RetryVideo(DownloadItem item) { if (item.Status != DownloadStatus.Completed) _mgr.Retry(item); }
    [RelayCommand] void OpenFolder(DownloadItem i) => FileActions.ShowInFolder(i.FilePath, i.OutputFolder);
    [RelayCommand] void Redownload(DownloadItem i) => _mgr.Retry(i);
    [RelayCommand] void Remove(DownloadItem i) => _mgr.Remove(new[] { i }, false);

    [RelayCommand]
    void ClearAll()
    {
        if (Items.Count == 0) return;
        if (Confirm != null && !Confirm(Loc.T("S.ClearConfirm"))) return;
        _mgr.Remove(Items.ToList(), false);
    }
}
