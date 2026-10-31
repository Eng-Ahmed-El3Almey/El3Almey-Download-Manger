using System.Text.Json;

namespace El3Almey.Services;

public class AppSettings
{
    public string Language { get; set; } = "en";
    public string DownloadFolder { get; set; } = WindowsLibraryPaths.VideoFolder;
    public string AudioDownloadFolder { get; set; } = WindowsLibraryPaths.AudioFolder;
    public bool DefaultIsAudio { get; set; } = false;
    public int DefaultVideoHeight { get; set; } = 1080;
    public int DefaultAudioKbps { get; set; } = 192;
    public bool AddThumbnailToMp3 { get; set; } = true;
    public bool CreatePlaylistSubfolder { get; set; } = true;
    public bool NumberPlaylistItems { get; set; } = false;
    public bool IncludeChaptersByDefault { get; set; } = false;
    public int MaxConcurrent { get; set; } = 3;
    public bool AutoStartQueue { get; set; } = true;
    public bool SleepWhenDownloadsFinish { get; set; } = false;
    public bool ShowDownloadCompletedNotification { get; set; } = true;
    public bool PlaySoundNotification { get; set; } = false;
    public string FfmpegPath { get; set; } = "";
    // One-time migration marker: move the default destinations into Windows Videos/Music libraries.
    public bool WindowsLibrariesDefaultsApplied { get; set; }
}

public static class SettingsStore
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "El3Almey");
    static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Current { get; private set; } = new();
    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            var oldVideo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "El3Almey Downloads");
            if (!Current.WindowsLibrariesDefaultsApplied)
            {
                // Apply the requested Windows library locations once on upgrade. Later user
                // changes in Settings are preserved on subsequent launches.
                Current.DownloadFolder = WindowsLibraryPaths.VideoFolder;
                Current.AudioDownloadFolder = WindowsLibraryPaths.AudioFolder;
                Current.WindowsLibrariesDefaultsApplied = true;
                Save();
            }
            else
            {
                if (string.Equals(Current.DownloadFolder, oldVideo, StringComparison.OrdinalIgnoreCase)) Current.DownloadFolder = WindowsLibraryPaths.VideoFolder;
                if (string.IsNullOrWhiteSpace(Current.DownloadFolder)) Current.DownloadFolder = WindowsLibraryPaths.VideoFolder;
                if (string.IsNullOrWhiteSpace(Current.AudioDownloadFolder)) Current.AudioDownloadFolder = WindowsLibraryPaths.AudioFolder;
            }
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        Changed?.Invoke();
    }
}


public static class WindowsLibraryPaths
{
    public static string VideoFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "El3Almey Download Manager");
    public static string AudioFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "El3Almey Download Manager");
    public static string EnsureVideoFolder() { var p = VideoFolder; Directory.CreateDirectory(p); return p; }
    public static string EnsureAudioFolder() { var p = AudioFolder; Directory.CreateDirectory(p); return p; }
}
