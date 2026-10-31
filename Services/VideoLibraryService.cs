using System.Text.Json;

namespace El3Almey.Services;

public sealed class VideoLibraryService
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "El3AlmeyDownloadManager");
    private static readonly string DataFile = Path.Combine(DataDirectory, "video-library.json");
    private static readonly string[] Extensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v",
        ".mpeg", ".mpg", ".ts", ".flv", ".3gp"
    };

    private sealed class LibraryData
    {
        public List<string> Folders { get; set; } = new();
        public List<string> RecentFiles { get; set; } = new();
    }

    private LibraryData _data;

    public VideoLibraryService()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            _data = File.Exists(DataFile)
                ? JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(DataFile)) ?? new()
                : new();
        }
        catch
        {
            _data = new();
        }

        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (Directory.Exists(videos) && !_data.Folders.Contains(videos, StringComparer.OrdinalIgnoreCase))
            _data.Folders.Insert(0, videos);
        Save();
    }

    public IReadOnlyList<string> Folders => _data.Folders;

    public bool AddFolder(string folder)
    {
        if (!Directory.Exists(folder) ||
            _data.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            return false;

        _data.Folders.Add(folder);
        Save();
        return true;
    }

    public IReadOnlyList<string> GetVideos()
    {
        var result = new List<string>();
        foreach (string folder in _data.Folders)
        {
            if (!Directory.Exists(folder))
                continue;

            try
            {
                result.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void AddRecent(string file)
    {
        _data.RecentFiles.RemoveAll(path => string.Equals(path, file, StringComparison.OrdinalIgnoreCase));
        _data.RecentFiles.Insert(0, file);
        _data.RecentFiles = _data.RecentFiles.Take(30).ToList();
        Save();
    }

    public IReadOnlyList<string> GetRecent() =>
        _data.RecentFiles.Where(File.Exists).ToList();

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(DataFile, JsonSerializer.Serialize(_data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
