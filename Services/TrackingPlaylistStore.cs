using System.Text.Json;

namespace El3Almey.Services;

public sealed class TrackedPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastCheckedAt { get; set; }
    public List<string> KnownVideoIds { get; set; } = new();
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
