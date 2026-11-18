using System.Text.Json;
using System.ComponentModel;

namespace El3Almey.Services;

public sealed class TrackedPlaylist : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    int _newVideoCount;
    DateTime? _lastCheckedAt;
    public int NewVideoCount { get => _newVideoCount; set { if (_newVideoCount == value) return; _newVideoCount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewVideoCount))); } }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastCheckedAt { get => _lastCheckedAt; set { if (_lastCheckedAt == value) return; _lastCheckedAt = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastCheckedAt))); } }
    public List<string> KnownVideoIds { get; set; } = new();
    public List<string> PendingNewVideoIds { get; set; } = new();
    public bool IsAudio { get; set; }
    public int Height { get; set; } = 1080;
    public int Kbps { get; set; } = 192;
    public string Format { get; set; } = "MP4";
    public string AudioTrackLanguage { get; set; } = "default";
    public string SubtitleTrackLanguage { get; set; } = "none";
    public bool IncludeChapters { get; set; } = true;
    public string OutputFolder { get; set; } = "";
    public bool CreateSubfolder { get; set; } = true;
    public bool NumberItems { get; set; }
    public int NumberWidth { get; set; } = 2;
    public int LastPlaylistIndex { get; set; }

    public string PlaylistThumbnail => !string.IsNullOrWhiteSpace(Thumbnail)
        ? Thumbnail
        : KnownVideoIds.Count > 0 ? YouTubeService.Thumb(KnownVideoIds[0], "hqdefault") : "";
}

public static class TrackingPlaylistStore
{
    static string FilePath => Path.Combine(SettingsStore.DataDir, "tracked-playlists.json");
    static readonly object Sync = new();

    public static List<TrackedPlaylist> Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(FilePath)) return new();
                return JsonSerializer.Deserialize<List<TrackedPlaylist>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { return new(); }
        }
    }

    public static void Save(IEnumerable<TrackedPlaylist> playlists)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(SettingsStore.DataDir);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(playlists, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
    }
}
