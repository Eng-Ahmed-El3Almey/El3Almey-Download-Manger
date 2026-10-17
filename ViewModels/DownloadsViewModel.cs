using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public class SortOption
{
    public string Key { get; init; } = "";
    public override string ToString() => Loc.T("S.Sort." + Key);
}

public partial class DownloadsViewModel : ObservableObject
{
    readonly DownloadManager _mgr;
    List<DownloadItem> _pendingDelete = new();

    public ObservableCollection<DownloadItem> Items { get; } = new();
    public ObservableCollection<DownloadItem> PlaylistItems { get; } = new();
    [ObservableProperty] private bool _isPlaylistDetailOpen;
    [ObservableProperty] private string _playlistDetailTitle = "";
    [ObservableProperty] private string _playlistDetailSummary = "";
    public SortOption[] SortOptions { get; } =
        { new() { Key = "Date" }, new() { Key = "Name" }, new() { Key = "Status" }, new() { Key = "Progress" } };

    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SortOption _selectedSort;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isDeleteDialogOpen;
    [ObservableProperty] private string _deleteMessage = "";
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastTitle = "";
    [ObservableProperty] private string _toastSubtitle = "";
    [ObservableProperty] private bool _allSelected;

    public Action<string>? CopyText { get; set; }

    public bool HasSelection => SelectedCount > 0;
    public string SelectedText => $"{SelectedCount} {Loc.T("S.ItemsSelected")}";
    IEnumerable<DownloadItem> TopLevelItems => _mgr.Items.Where(i => !i.IsPlaylist || i.IsPlaylistRepresentative);
    public int AllCount => TopLevelItems.Count();
    int Count(DownloadStatus s) => TopLevelItems.Count(i => i.Status == s);
    string L(string key, int n) => $"{Loc.T(key)} ({n})";
    public string DownloadingLabel => L("S.St.Downloading", Count(DownloadStatus.Downloading));
    public string PausedLabel => L("S.St.Paused", Count(DownloadStatus.Paused));
    public string CompletedLabel => L("S.St.Completed", Count(DownloadStatus.Completed));
    public string FailedLabel => L("S.St.Failed", Count(DownloadStatus.Failed));
    public string QueuedLabel => L("S.St.Queued", Count(DownloadStatus.Queued));
    public string FooterText =>
        $"{AllCount} {Loc.T("S.Items")}  •  {Count(DownloadStatus.Downloading)} {Loc.T("S.St.Downloading")}  •  " +
        $"{Count(DownloadStatus.Paused)} {Loc.T("S.St.Paused")}  •  {Count(DownloadStatus.Completed)} {Loc.T("S.St.Completed")}  •  " +
        $"{Count(DownloadStatus.Failed)} {Loc.T("S.St.Failed")}";

    public DownloadsViewModel(DownloadManager mgr)
    {
        _mgr = mgr;
        _selectedSort = SortOptions[0];
        _mgr.Changed += Refresh;
        _mgr.ItemChanged += (_, p) =>
        {
            if (p == nameof(DownloadItem.IsSelected)) SelectedCount = _mgr.Items.Count(i => i.IsSelected);
        };
        Loc.Changed += () =>
        {
            foreach (var i in _mgr.Items) i.RefreshLanguage();
            RaiseLabels();
            OnPropertyChanged(nameof(SelectedText));
        };
        Refresh();
    }

    partial void OnFilterChanged(string value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedSortChanged(SortOption value) => Refresh();
    partial void OnSelectedCountChanged(int value) { OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedText)); }
    partial void OnAllSelectedChanged(bool value) { foreach (var i in Items) i.IsSelected = value; }

    void RaiseLabels()
    {
        foreach (var n in new[] { nameof(AllCount), nameof(DownloadingLabel), nameof(PausedLabel),
                                  nameof(FailedLabel), nameof(QueuedLabel), nameof(FooterText) })
            OnPropertyChanged(n);
    }

    public void Refresh()
    {
        if (IsPlaylistDetailOpen && PlaylistItems.Count > 0)
        {
            PlaylistDetailSummary = BuildPlaylistSummary(PlaylistItems);
        }
        IEnumerable<DownloadItem> q = _mgr.Items.Where(i => !i.IsPlaylist
            ? i.Status != DownloadStatus.Completed
            : i.IsPlaylistRepresentative && _mgr.Items.Any(child => child.PlaylistGroupId == i.PlaylistGroupId && child.Status != DownloadStatus.Completed));
        if (Filter != "All" && Enum.TryParse<DownloadStatus>(Filter, out var st))
            q = q.Where(i => i.IsPlaylistRepresentative
                ? _mgr.Items.Any(child => child.PlaylistGroupId == i.PlaylistGroupId && child.Status == st)
                : i.Status == st);
        if (!string.IsNullOrWhiteSpace(SearchText))
            q = q.Where(i => i.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        q = SelectedSort.Key switch
        {
            "Name" => q.OrderBy(i => i.DisplayTitle),
            "Status" => q.OrderBy(i => i.Status),
            "Progress" => q.OrderByDescending(i => i.Progress),
            _ => q.OrderByDescending(i => i.AddedAt)
        };
        var list = q.ToList();

        // Status changes are frequent while a download starts/finishes. Do not tear down
        // and rebuild the bound list when its membership and order have not changed.
        // Clearing/re-adding every row forces WPF to recreate containers and was a major
        // source of lag when navigating to Downloads or starting a download.
        bool sameItems = Items.Count == list.Count;
        if (sameItems)
        {
            for (int index = 0; index < list.Count; index++)
            {
                if (!ReferenceEquals(Items[index], list[index]))
                {
                    sameItems = false;
                    break;
                }
            }
        }

        if (!sameItems)
        {
            Items.Clear();
            foreach (var i in list) Items.Add(i);
        }

        SelectedCount = _mgr.Items.Count(i => i.IsSelected);
        RaiseLabels();
    }


    public void OpenPlaylist(DownloadItem item)
    {
        if (!item.IsPlaylist) return;
        var children = _mgr.Items.Where(i => i.PlaylistGroupId == item.PlaylistGroupId)
            .OrderBy(i => i.PlaylistIndex).ToList();
        PlaylistItems.Clear();
        foreach (var child in children) PlaylistItems.Add(child);
        PlaylistDetailTitle = item.PlaylistTitle;
        PlaylistDetailSummary = BuildPlaylistSummary(children);
        IsPlaylistDetailOpen = true;
    }

    static string BuildPlaylistSummary(IReadOnlyCollection<DownloadItem> children)
    {
        long knownBytes = children.Sum(i => Math.Max(0, i.TotalBytes));
        string size = knownBytes > 0 ? $" • Total size: {Fmt.Bytes(knownBytes)}" : " • Total size unavailable";
        return $"{children.Count} videos • {children.Count(i => i.Status == DownloadStatus.Completed)} completed • {children.Count(i => i.Status == DownloadStatus.Failed)} failed{size}";
    }

    [RelayCommand] void OpenPlaylistDetails(DownloadItem item) => OpenPlaylist(item);
    [RelayCommand] void BackFromPlaylist() => IsPlaylistDetailOpen = false;
    [RelayCommand] void PlaylistItemAction(DownloadItem item) => PrimaryAction(item);
    [RelayCommand] void OpenPlaylistFolder(DownloadItem item) => FileActions.ShowInFolder(item.FilePath, item.OutputFolder);

    async void Toast(string title, string sub)
    {
        ToastTitle = title; ToastSubtitle = sub; IsToastVisible = true;
        await Task.Delay(3000);
        IsToastVisible = false;
    }

    void CloseMenus() { foreach (var i in _mgr.Items) i.IsMenuOpen = false; }

    [RelayCommand] void SetFilter(string f) => Filter = f;

    [RelayCommand]
    void PrimaryAction(DownloadItem i)
    {
        if (i.IsPlaylist)
        {
            var children = _mgr.Items.Where(x => x.PlaylistGroupId == i.PlaylistGroupId).ToList();
            if (children.Any(x => x.Status == DownloadStatus.Downloading)) foreach (var child in children) _mgr.Pause(child);
            else foreach (var child in children.Where(x => x.Status != DownloadStatus.Completed)) _mgr.Resume(child);
            return;
        }
        switch (i.Status)
        {
            case DownloadStatus.Downloading: _mgr.Pause(i); break;
            case DownloadStatus.Completed: OpenFile(i); break;
            case DownloadStatus.Failed: _mgr.Retry(i); break;
            default: _mgr.Resume(i); break;
        }
    }

    [RelayCommand]
    void OpenFile(DownloadItem i)
    {
        CloseMenus();
        if (FileActions.OpenFile(i.FilePath)) Toast(Loc.T("S.FileOpened"), System.IO.Path.GetFileName(i.FilePath));
        else Toast(Loc.T("S.FileMissing"), i.Title);
    }

    [RelayCommand]
    void OpenFolder(DownloadItem i) { CloseMenus(); FileActions.ShowInFolder(i.FilePath, i.OutputFolder); }

    [RelayCommand] void CopyTitle(DownloadItem i) { CloseMenus(); CopyText?.Invoke(i.Title); Toast(Loc.T("S.Copied"), i.Title); }
    [RelayCommand] void CopyUrl(DownloadItem i) { CloseMenus(); CopyText?.Invoke(i.Url); Toast(Loc.T("S.Copied"), i.Url); }
    [RelayCommand]
    void RetryVideo(DownloadItem i)
    {
        // Used by the per-video Retry button inside playlist details: retry only this file,
        // even when it is also the playlist's representative item.
        CloseMenus();
        if (i.Status == DownloadStatus.Completed) return;
        _mgr.Retry(i);
        Toast(Loc.T("S.Retry"), i.Title);
    }

    [RelayCommand]
    void Retry(DownloadItem i)
    {
        CloseMenus();
        if (i.IsPlaylistRepresentative)
        {
            var children = _mgr.Items.Where(x => x.IsPlaylist && x.PlaylistGroupId == i.PlaylistGroupId).ToList();
            var failed = children.Where(x => x.Status == DownloadStatus.Failed).ToList();
            // Retry failed videos first. If none failed, restart only unfinished videos.
            var targets = failed.Count > 0 ? failed : children.Where(x => x.Status != DownloadStatus.Completed).ToList();
            foreach (var child in targets) _mgr.Retry(child);
            Toast(Loc.T("S.Retry"), $"{targets.Count} videos queued");
            return;
        }
        _mgr.Retry(i);
    }

    [RelayCommand] void AddToQueue(DownloadItem i)
    {
        CloseMenus();
        if (i.IsPlaylistRepresentative)
        {
            foreach (var child in _mgr.Items.Where(x => x.IsPlaylist && x.PlaylistGroupId == i.PlaylistGroupId && x.Status != DownloadStatus.Completed))
                _mgr.Resume(child);
            return;
        }
        _mgr.Resume(i);
    }
    [RelayCommand] void RemoveFromHistory(DownloadItem i) { CloseMenus(); _mgr.Remove(new[] { i }, false); }
    [RelayCommand] void AskDelete(DownloadItem i) { CloseMenus(); AskDeleteMany(new[] { i }); }

    void AskDeleteMany(IEnumerable<DownloadItem> items)
    {
        _pendingDelete = items.ToList();
        if (_pendingDelete.Count == 0) return;
        DeleteMessage = _pendingDelete.Count == 1
            ? (string.IsNullOrEmpty(_pendingDelete[0].FilePath) ? _pendingDelete[0].Title : System.IO.Path.GetFileName(_pendingDelete[0].FilePath))
            : $"{_pendingDelete.Count} {Loc.T("S.Items")}";
        IsDeleteDialogOpen = true;
    }

    [RelayCommand]
    void ConfirmDelete()
    {
        _mgr.Remove(_pendingDelete, true);
        _pendingDelete.Clear();
        IsDeleteDialogOpen = false;
    }
    [RelayCommand] void CancelDelete() { _pendingDelete.Clear(); IsDeleteDialogOpen = false; }

    [RelayCommand] void PauseAll() => _mgr.PauseAll();
    [RelayCommand] void ResumeAll() => _mgr.ResumeAll();

    IEnumerable<DownloadItem> Selected => _mgr.Items.Where(i => i.IsSelected ||
        (i.IsPlaylist && _mgr.Items.Any(rep => rep.IsSelected && rep.PlaylistGroupId == i.PlaylistGroupId))).Distinct().ToList();
    [RelayCommand] void PauseSelected() { foreach (var i in Selected) _mgr.Pause(i); }
    [RelayCommand] void ResumeSelected() { foreach (var i in Selected.Where(x => x.Status != DownloadStatus.Completed)) _mgr.Resume(i); }
    [RelayCommand] void RemoveSelected() => _mgr.Remove(Selected, false);
    [RelayCommand] void DeleteSelected() => AskDeleteMany(Selected);
    [RelayCommand] void ClearSelection() { foreach (var i in _mgr.Items) i.IsSelected = false; AllSelected = false; }
    [RelayCommand] void CloseToast() => IsToastVisible = false;
}
