using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public partial class TrackedPlaylistViewModel : ObservableObject
{
    readonly DownloadManager _manager;
    readonly ObservableCollection<TrackedPlaylist> _records = new();
    [ObservableProperty] private TrackedPlaylist? _selectedPlaylist;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _newVideoCount;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _allVideosSelected;

    public ObservableCollection<TrackedPlaylist> Playlists { get; } = new();
    public ObservableCollection<EntryInfo> NewVideos { get; } = new();
    public ObservableCollection<EntryInfo> VisibleNewVideos { get; } = new();
    public bool HasNewVideos => NewVideos.Count > 0;
    public string PlaylistCountText => Playlists.Count.ToString();
    public int TotalTrackedVideoCount => Playlists.Sum(x => x.KnownVideoIds.Count + x.PendingNewVideoIds.Count);
    public string TotalTrackedVideoCountText => TotalTrackedVideoCount.ToString();
    public int SelectedVideoCount => NewVideos.Count(x => x.IsSelected);
    public bool HasSelectedVideos => SelectedVideoCount > 0;
    public bool HasSelectedPlaylist => SelectedPlaylist != null;
    public bool CanCheck => HasSelectedPlaylist && !IsChecking;
    public bool CanCheckAll => Playlists.Count > 0 && !IsChecking;

    partial void OnSelectedPlaylistChanged(TrackedPlaylist? value)
    {
        OnPropertyChanged(nameof(HasSelectedPlaylist));
        OnPropertyChanged(nameof(CanCheck));
        NewVideos.Clear();
        NewVideoCount = value?.NewVideoCount ?? 0;
        OnPropertyChanged(nameof(HasNewVideos));
        OnPropertyChanged(nameof(LastCheckedText));
        RestorePendingRows(value);
    }
    partial void OnSearchTextChanged(string value) => RefreshVisibleVideos();
    partial void OnAllVideosSelectedChanged(bool value)
    {
        foreach (var video in NewVideos) video.IsSelected = value;
        RefreshVisibleVideos();
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(HasSelectedVideos));
    }
    void RefreshVisibleVideos()
    {
        VisibleNewVideos.Clear();
        IEnumerable<EntryInfo> query = NewVideos;
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(x => x.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || x.Channel.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var video in query) VisibleNewVideos.Add(video);
        OnPropertyChanged(nameof(SelectedVideoCount));
        OnPropertyChanged(nameof(HasSelectedVideos));
    }
    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanCheckAll));
    }
    public string LastCheckedText => SelectedPlaylist?.LastCheckedAt?.ToString("g") ?? (Loc.Language == "ar" ? "لم يتم الفحص بعد" : "Never checked");

    public TrackedPlaylistViewModel(DownloadManager manager)
    {
        _manager = manager;
        foreach (var item in TrackingPlaylistStore.Load()) { _records.Add(item); Playlists.Add(item); }
        if (Playlists.Count > 0) SelectedPlaylist = Playlists[0];
        Loc.Changed += RefreshLabels;
    }

    void RefreshLabels()
    {
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(PlaylistCountText));
        OnPropertyChanged(nameof(TotalTrackedVideoCount));
        OnPropertyChanged(nameof(TotalTrackedVideoCountText));
    }

    public void Reload()
    {
        var selectedId = SelectedPlaylist?.Id;
        _records.Clear();
        Playlists.Clear();
        foreach (var item in TrackingPlaylistStore.Load()) { _records.Add(item); Playlists.Add(item); }
        SelectedPlaylist = Playlists.FirstOrDefault(x => x.Id == selectedId) ?? Playlists.FirstOrDefault();
        NewVideos.Clear();
        NewVideoCount = SelectedPlaylist?.NewVideoCount ?? 0;
        RestorePendingRows(SelectedPlaylist);
        OnPropertyChanged(nameof(PlaylistCountText));
        OnPropertyChanged(nameof(TotalTrackedVideoCount));
        OnPropertyChanged(nameof(TotalTrackedVideoCountText));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(HasNewVideos));
        OnPropertyChanged(nameof(CanCheckAll));
    }

    public bool Add(TrackedPlaylist item)
    {
        if (_records.Any(x => x.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase))) return false;
        item.NewVideoCount = 0;
        _records.Add(item);
        Playlists.Add(item);
        SelectedPlaylist = item;
        TrackingPlaylistStore.Save(_records);
        OnPropertyChanged(nameof(PlaylistCountText));
        OnPropertyChanged(nameof(TotalTrackedVideoCount));
        OnPropertyChanged(nameof(TotalTrackedVideoCountText));
        OnPropertyChanged(nameof(CanCheckAll));
        return true;
    }

    [RelayCommand]
    async Task CheckSelected()
    {
        var item = SelectedPlaylist;
        if (item == null || IsChecking) return;
        IsChecking = true;
        StatusMessage = Loc.Language == "ar" ? "جاري فحص قائمة التشغيل..." : "Checking playlist...";
        try
        {
            await CheckPlaylistAsync(item, true);
            StatusMessage = item.NewVideoCount == 0
                ? (Loc.Language == "ar" ? "القائمة محدثة. لا توجد فيديوهات جديدة." : "You're up to date. No new videos found.")
                : (Loc.Language == "ar" ? $"تم العثور على {item.NewVideoCount} فيديو جديد." : $"{item.NewVideoCount} new video(s) found.");
        }
        catch (Exception ex)
        {
            StatusMessage = (Loc.Language == "ar" ? "فشل الفحص: " : "Check failed: ") + ex.Message;
        }
        finally { IsChecking = false; }
    }

    [RelayCommand]
    async Task CheckAll()
    {
        if (IsChecking || Playlists.Count == 0) return;
        IsChecking = true;
        int succeeded = 0;
        int totalNew = 0;
        var errors = new List<string>();
        try
        {
            foreach (var item in Playlists.ToList())
            {
                StatusMessage = (Loc.Language == "ar" ? "جاري فحص: " : "Checking: ") + item.Title;
                try
                {
                    await CheckPlaylistAsync(item, ReferenceEquals(item, SelectedPlaylist));
                    succeeded++;
                    totalNew += item.NewVideoCount;
                }
                catch (Exception ex)
                {
                    errors.Add(item.Title + ": " + ex.Message);
                }
            }

            StatusMessage = errors.Count == 0
                ? (Loc.Language == "ar"
                    ? $"اكتمل فحص {succeeded} قائمة. تم العثور على {totalNew} فيديو جديد."
                    : $"Checked {succeeded} playlists. Found {totalNew} new videos.")
                : (Loc.Language == "ar"
                    ? $"اكتمل فحص {succeeded} قائمة، وفشل {errors.Count}."
                    : $"Checked {succeeded} playlists; {errors.Count} failed.");
        }
        finally
        {
            TrackingPlaylistStore.Save(_records);
            OnPropertyChanged(nameof(LastCheckedText));
            OnPropertyChanged(nameof(HasNewVideos));
            IsChecking = false;
            RestorePendingRows(SelectedPlaylist);
        }
    }

    void RestorePendingRows(TrackedPlaylist? item)
    {
        NewVideos.Clear();
        VisibleNewVideos.Clear();
        if (item != null)
            foreach (var entry in item.PendingNewVideos)
            {
                entry.IsSelected = true;
                AddVideoRow(entry);
            }
        AllVideosSelected = NewVideos.Count > 0;
        RefreshVisibleVideos();
        NewVideoCount = item?.NewVideoCount ?? 0;
        OnPropertyChanged(nameof(HasNewVideos));
    }

    void AddVideoRow(EntryInfo entry)
    {
        entry.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EntryInfo.IsSelected))
            {
                _allVideosSelected = NewVideos.Count > 0 && NewVideos.All(x => x.IsSelected);
                OnPropertyChanged(nameof(AllVideosSelected));
                OnPropertyChanged(nameof(SelectedVideoCount));
                OnPropertyChanged(nameof(HasSelectedVideos));
            }
        };
        NewVideos.Add(entry);
    }

    async Task CheckPlaylistAsync(TrackedPlaylist item, bool populateSelected)
    {
        var result = await YtDlpService.GetPlaylistMetadataAsync(item.Url, 1000, CancellationToken.None);
        if (result.Entries.Count == 0)
            throw new InvalidOperationException("No available videos were returned for this playlist.");

        if (!string.IsNullOrWhiteSpace(result.Title)) item.Title = result.Title;
        if (!string.IsNullOrWhiteSpace(result.Channel)) item.Channel = result.Channel;
        if (!string.IsNullOrWhiteSpace(result.Thumbnail))
            item.Thumbnail = result.Thumbnail;
        else if (string.IsNullOrWhiteSpace(item.Thumbnail))
            item.Thumbnail = result.Entries.FirstOrDefault()?.Thumbnail
                ?? YouTubeService.Thumb(result.Entries[0].Id, "hqdefault");

        var known = new HashSet<string>(item.KnownVideoIds, StringComparer.OrdinalIgnoreCase);
        var pending = new HashSet<string>(item.PendingNewVideoIds, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result.Entries)
        {
            if (!known.Contains(entry.Id))
                pending.Add(entry.Id);
        }

        item.PendingNewVideoIds = pending.ToList();
        var pendingRows = new List<EntryInfo>();
        int index = 0;
        foreach (var entry in result.Entries)
        {
            index++;
            if (!pending.Contains(entry.Id)) continue;
            pendingRows.Add(new EntryInfo
            {
                Index = index,
                Id = entry.Id,
                Title = string.IsNullOrWhiteSpace(entry.Title) ? "Video " + index : entry.Title,
                Channel = string.IsNullOrWhiteSpace(entry.Channel) ? result.Channel : entry.Channel,
                Duration = entry.Duration,
                QualityText = item.IsAudio ? $"{item.Kbps} kbps" : $"{item.Height}p",
                VideoFormats = entry.VideoFormats ?? new List<YtDlpService.VideoFormatEstimate>(),
                SizeText = EstimateSizeText(item, entry),
                Thumb = string.IsNullOrWhiteSpace(entry.Thumbnail)
                    ? YouTubeService.Thumb(entry.Id, "mqdefault") : entry.Thumbnail
            });
        }

        item.PendingNewVideos = pendingRows;
        item.NewVideoCount = item.PendingNewVideoIds.Count;
        item.LastCheckedAt = DateTime.Now;
        if (populateSelected && ReferenceEquals(item, SelectedPlaylist))
        {
            NewVideos.Clear();
            foreach (var entry in pendingRows) AddVideoRow(entry);
            AllVideosSelected = NewVideos.Count > 0;
            RefreshVisibleVideos();
            NewVideoCount = item.NewVideoCount;
            OnPropertyChanged(nameof(HasNewVideos));
        }
        TrackingPlaylistStore.Save(_records);
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(TotalTrackedVideoCount));
        OnPropertyChanged(nameof(TotalTrackedVideoCountText));
    }

    [RelayCommand]
    void DownloadNewVideos()
    {
        var item = SelectedPlaylist;
        if (item == null || NewVideos.Count == 0) return;
        string root = string.IsNullOrWhiteSpace(item.OutputFolder)
            ? (item.IsAudio ? WindowsLibraryPaths.AudioFolder : WindowsLibraryPaths.VideoFolder) : item.OutputFolder;
        string folder = item.CreateSubfolder ? Path.Combine(root, Fmt.Sanitize(item.Title)) : root;
        Directory.CreateDirectory(folder);
        int width = Math.Max(item.NumberWidth, resultWidth(NewVideos.Select(x => x.Index).DefaultIfEmpty(1).Max()));
        string groupId = Guid.NewGuid().ToString("N");
        var entries = NewVideos.Where(x => x.IsSelected).ToList();
        if (entries.Count == 0) return;
        var downloads = new List<DownloadItem>();
        foreach (var entry in entries)
        {
            int index = Math.Max(++item.LastPlaylistIndex, entry.Index);
            string name = item.NumberItems ? $"{index.ToString().PadLeft(width, '0')} - {entry.Title}" : entry.Title;
            long bytes = item.IsAudio
                ? (long)((entry.Duration?.TotalSeconds ?? 0) * item.Kbps * 125)
                : entry.VideoFormats.Where(x => x.Height <= item.Height).OrderByDescending(x => x.Height).Select(x => x.EstimatedBytes).FirstOrDefault();
            downloads.Add(new DownloadItem
            {
                VideoId = entry.Id, Title = entry.Title, Thumbnail = YouTubeService.Thumb(entry.Id, "mqdefault"),
                Url = "https://www.youtube.com/watch?v=" + entry.Id, IsAudio = item.IsAudio,
                Height = item.Height, Kbps = item.Kbps, Format = item.Format,
                AudioTrackLanguage = item.AudioTrackLanguage, SubtitleTrackLanguage = item.SubtitleTrackLanguage,
                IncludeChapters = item.IncludeChapters, AddThumbnail = item.IsAudio,
                OutputFolder = folder, FileBaseName = name,
                Details = item.IsAudio ? $"{item.Format} • {item.Kbps}kbps" : $"{item.Format} • {item.Height}p",
                TotalBytes = bytes, PlaylistGroupId = groupId, PlaylistUrl = item.Url, PlaylistTitle = item.Title,
                PlaylistIndex = downloads.Count + 1, PlaylistCount = entries.Count
            });
        }
        foreach (var download in downloads) download.PlaylistCount = downloads.Count;
        _manager.Add(downloads, true, respectAutoStart: false);
        var queuedIds = entries.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        item.KnownVideoIds.AddRange(queuedIds.Where(id => !item.KnownVideoIds.Contains(id, StringComparer.OrdinalIgnoreCase)));
        item.PendingNewVideoIds.RemoveAll(id => queuedIds.Contains(id));
        item.PendingNewVideos.RemoveAll(video => queuedIds.Contains(video.Id));
        item.NewVideoCount = item.PendingNewVideoIds.Count;
        TrackingPlaylistStore.Save(_records);
        NewVideos.Clear();
        VisibleNewVideos.Clear();
        NewVideoCount = item.NewVideoCount;
        StatusMessage = Loc.Language == "ar" ? "تمت إضافة الفيديوهات المحددة إلى التنزيلات." : "Selected new videos added to Downloads.";
        OnPropertyChanged(nameof(HasNewVideos));
    }

    static string EstimateSizeText(TrackedPlaylist playlist, EntryInfo entry)
    {
        if (playlist.IsAudio && entry.Duration.HasValue)
            return Fmt.Bytes((long)(entry.Duration.Value.TotalSeconds * playlist.Kbps * 125));
        var estimate = entry.VideoFormats
            .Where(x => x.Height <= playlist.Height)
            .OrderByDescending(x => x.Height)
            .Select(x => x.EstimatedBytes)
            .FirstOrDefault();
        return estimate > 0 ? Fmt.Bytes(estimate) : "—";
    }

    public bool AddFromDownload(DownloadItem download, IEnumerable<DownloadItem> groupItems)
    {
        if (!download.IsPlaylistRepresentative || string.IsNullOrWhiteSpace(download.PlaylistUrl)) return false;
        var children = groupItems.Where(x => x.IsPlaylist && x.PlaylistGroupId == download.PlaylistGroupId)
            .OrderBy(x => x.PlaylistIndex).ToList();
        if (children.Count == 0) return false;
        var item = new TrackedPlaylist
        {
            Url = download.PlaylistUrl,
            Title = string.IsNullOrWhiteSpace(download.PlaylistTitle) ? download.Title : download.PlaylistTitle,
            Channel = "",
            Thumbnail = download.Thumbnail ?? "",
            KnownVideoIds = children.Select(x => x.VideoId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsAudio = download.IsAudio,
            Height = download.Height,
            Kbps = download.Kbps,
            Format = download.Format,
            AudioTrackLanguage = download.AudioTrackLanguage,
            SubtitleTrackLanguage = download.SubtitleTrackLanguage,
            IncludeChapters = download.IncludeChapters,
            OutputFolder = download.OutputFolder,
            CreateSubfolder = true,
            LastPlaylistIndex = children.Count
        };
        return Add(item);
    }

    static int resultWidth(int value) => Math.Max(2, value.ToString().Length);

    [RelayCommand]
    void RemoveSelected()
    {
        if (SelectedPlaylist == null) return;
        var old = SelectedPlaylist;
        _records.Remove(old);
        Playlists.Remove(old);
        SelectedPlaylist = Playlists.FirstOrDefault();
        TrackingPlaylistStore.Save(_records);
        NewVideos.Clear();
        NewVideoCount = SelectedPlaylist?.NewVideoCount ?? 0;
        OnPropertyChanged(nameof(PlaylistCountText));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(HasNewVideos));
        OnPropertyChanged(nameof(CanCheckAll));
    }
}