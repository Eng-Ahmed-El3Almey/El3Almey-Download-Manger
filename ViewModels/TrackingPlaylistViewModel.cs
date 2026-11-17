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

    public ObservableCollection<TrackedPlaylist> Playlists { get; } = new();
    public ObservableCollection<EntryInfo> NewVideos { get; } = new();
    public bool HasNewVideos => NewVideos.Count > 0;
    public string PlaylistCountText => Playlists.Count.ToString();\n    public bool HasSelectedPlaylist => SelectedPlaylist != null;\n    partial void OnSelectedPlaylistChanged(TrackedPlaylist? value) => OnPropertyChanged(nameof(HasSelectedPlaylist));
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
    }

    public bool Add(TrackedPlaylist item)
    {
        if (_records.Any(x => x.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase))) return false;
        _records.Add(item);
        Playlists.Add(item);
        SelectedPlaylist = item;
        TrackingPlaylistStore.Save(_records);
        OnPropertyChanged(nameof(PlaylistCountText));
        return true;
    }

    [RelayCommand]
    async Task CheckSelected()
    {
        var item = SelectedPlaylist;
        if (item == null || IsChecking) return;
        IsChecking = true;
        NewVideos.Clear();
        StatusMessage = Loc.Language == "ar" ? "جاري فحص قائمة التشغيل..." : "Checking playlist...";
        try
        {
            var result = await YouTubeService.ResolveAsync(item.Url, false, CancellationToken.None);
            if (!result.IsPlaylist) throw new InvalidOperationException("The saved URL no longer resolves to a playlist.");
            item.Title = result.PlaylistTitle;
            item.Channel = result.Channel;
            var known = new HashSet<string>(item.KnownVideoIds, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in result.Entries)
                if (!known.Contains(entry.Id)) NewVideos.Add(entry);
            item.LastCheckedAt = DateTime.Now;
            NewVideoCount = NewVideos.Count;
            StatusMessage = NewVideos.Count == 0
                ? (Loc.Language == "ar" ? "القائمة محدثة. لا توجد فيديوهات جديدة." : "You're up to date. No new videos found.")
                : (Loc.Language == "ar" ? $"تم العثور على {NewVideos.Count} فيديو جديد." : $"{NewVideos.Count} new video(s) found.");
            OnPropertyChanged(nameof(LastCheckedText));\n            OnPropertyChanged(nameof(HasNewVideos));
        }
        catch (Exception ex)
        {
            StatusMessage = (Loc.Language == "ar" ? "فشل الفحص: " : "Check failed: ") + ex.Message;
        }
        finally { IsChecking = false; }
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
        var entries = NewVideos.ToList();
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
                TotalBytes = bytes, PlaylistGroupId = groupId, PlaylistTitle = item.Title,
                PlaylistIndex = downloads.Count + 1, PlaylistCount = entries.Count
            });
            item.KnownVideoIds.Add(entry.Id);
        }
        foreach (var download in downloads) download.PlaylistCount = downloads.Count;
        TrackingPlaylistStore.Save(_records);
        _manager.Add(downloads, true, respectAutoStart: false);
        NewVideos.Clear();
        NewVideoCount = 0;
        StatusMessage = Loc.Language == "ar" ? "تمت إضافة الفيديوهات الجديدة إلى التنزيلات." : "New videos added to Downloads.";
        OnPropertyChanged(nameof(HasNewVideos));
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
        NewVideoCount = 0;
        OnPropertyChanged(nameof(PlaylistCountText));
        OnPropertyChanged(nameof(LastCheckedText));
        OnPropertyChanged(nameof(HasNewVideos));
    }
}
