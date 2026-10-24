using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Threading;
using El3Almey.ViewModels;

namespace El3Almey.Services;

public class DownloadRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string? Thumbnail { get; set; }
    public string Details { get; set; } = "";
    public bool IsAudio { get; set; }
    public int Height { get; set; }
    public int Kbps { get; set; }
    public string AudioTrackLanguage { get; set; } = "default";
    public string SubtitleTrackLanguage { get; set; } = "none";
    public bool AddThumbnail { get; set; }
    public bool IncludeChapters { get; set; }
    public string OutputFolder { get; set; } = "";
    public string FileBaseName { get; set; } = "";
    public string PlaylistGroupId { get; set; } = "";
    public string PlaylistTitle { get; set; } = "";
    public int PlaylistIndex { get; set; }
    public int PlaylistCount { get; set; }
    public string FilePath { get; set; } = "";
    public string Error { get; set; } = "";
    public DownloadStatus Status { get; set; }
    public long TotalBytes { get; set; }
    public double Progress { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class DownloadManager
{
    public ObservableCollection<DownloadItem> Items { get; } = new();
    public event Action? Changed;                                  // list or a status changed
    public event Action<DownloadItem>? DownloadCompleted;
    public event Action<DownloadItem, string>? ItemChanged;        // any property of any item

    readonly HashSet<string> _notifiedPlaylistGroups = new(StringComparer.Ordinal);
    readonly Dictionary<Guid, CancellationTokenSource> _running = new();
    static string StorePath => Path.Combine(SettingsStore.DataDir, "downloads.json");
    static AppSettings S => SettingsStore.Current;
    readonly DispatcherTimer _saveTimer;
    int _saveWriting;
    bool _saveAgain;
    long _saveVersion;
    long _lastWrittenVersion;
    static readonly object SaveFileLock = new();

    public DownloadManager()
    {
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _saveTimer.Tick += async (_, _) => await PersistDebouncedAsync();
        Load();
    }

    // ------------------------------------------------------------ persistence
    void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var list = JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(StorePath)) ?? new();
            foreach (var r in list)
            {
                var it = new DownloadItem
                {
                    Id = r.Id, Title = r.Title, Url = r.Url, VideoId = r.VideoId, Thumbnail = r.Thumbnail,
                    Details = r.Details, IsAudio = r.IsAudio, Height = r.Height, Kbps = r.Kbps,
                    AudioTrackLanguage = r.AudioTrackLanguage, SubtitleTrackLanguage = r.SubtitleTrackLanguage,
                    AddThumbnail = r.AddThumbnail, IncludeChapters = r.IncludeChapters, OutputFolder = r.OutputFolder, FileBaseName = r.FileBaseName,
                    PlaylistGroupId = r.PlaylistGroupId, PlaylistTitle = r.PlaylistTitle, PlaylistIndex = r.PlaylistIndex, PlaylistCount = r.PlaylistCount,
                    FilePath = r.FilePath, Error = r.Error, TotalBytes = r.TotalBytes,
                    AddedAt = r.AddedAt, FinishedAt = r.FinishedAt,
                    Status = r.Status is DownloadStatus.Downloading or DownloadStatus.Queued
                             ? DownloadStatus.Paused : r.Status,
                    Progress = r.Status == DownloadStatus.Completed ? 100 : Math.Clamp(r.Progress, 0, 99.9)
                };
                Hook(it);
                Items.Add(it);
            }
            foreach (var group in Items.Where(i => i.IsPlaylist).GroupBy(i => i.PlaylistGroupId))
                group.FirstOrDefault(i => i.IsPlaylistRepresentative)?.UpdatePlaylistSummary(group.ToList());
        }
        catch (Exception ex)
        {
            LogPersistenceError("load", ex);
        }
    }

    List<DownloadRecord> CreateSnapshot() => Items.Select(i => new DownloadRecord
    {
        Id = i.Id, Title = i.Title, Url = i.Url, VideoId = i.VideoId, Thumbnail = i.Thumbnail,
        Details = i.Details, IsAudio = i.IsAudio, Height = i.Height, Kbps = i.Kbps,
        AudioTrackLanguage = i.AudioTrackLanguage, SubtitleTrackLanguage = i.SubtitleTrackLanguage,
        AddThumbnail = i.AddThumbnail, IncludeChapters = i.IncludeChapters, OutputFolder = i.OutputFolder, FileBaseName = i.FileBaseName,
        PlaylistGroupId = i.PlaylistGroupId, PlaylistTitle = i.PlaylistTitle, PlaylistIndex = i.PlaylistIndex, PlaylistCount = i.PlaylistCount,
        FilePath = i.FilePath, Error = i.Error, Status = i.Status, TotalBytes = i.TotalBytes, Progress = i.Progress,
        AddedAt = i.AddedAt, FinishedAt = i.FinishedAt
    }).ToList();

    // Frequent state changes are coalesced. File serialization and disk writes run off the UI thread.
    void ScheduleSave()
    {
        if (_saveTimer.Dispatcher.CheckAccess())
        {
            if (!_saveTimer.IsEnabled) _saveTimer.Start();
        }
        else
        {
            _ = _saveTimer.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_saveTimer.IsEnabled) _saveTimer.Start();
            }), DispatcherPriority.Background);
        }
    }

    async Task PersistDebouncedAsync()
    {
        _saveTimer.Stop();
        if (Interlocked.CompareExchange(ref _saveWriting, 1, 0) != 0)
        {
            _saveAgain = true;
            return;
        }

        try
        {
            var snapshot = CreateSnapshot(); // ObservableCollection is read only on the UI thread.
            var version = Interlocked.Increment(ref _saveVersion);
            await Task.Run(() => WriteSnapshot(snapshot, version));
        }
        catch (Exception ex) { LogPersistenceError("save", ex); }
        finally
        {
            Interlocked.Exchange(ref _saveWriting, 0);
            if (_saveAgain)
            {
                _saveAgain = false;
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }
    }

    public void Save()
    {
        try
        {
            _saveTimer.Stop();
            var snapshot = CreateSnapshot();
            var version = Interlocked.Increment(ref _saveVersion);
            WriteSnapshot(snapshot, version);
        }
        catch (Exception ex) { LogPersistenceError("save", ex); }
    }

    void WriteSnapshot(List<DownloadRecord> list, long version)
    {
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = StorePath + ".tmp";
        lock (SaveFileLock)
        {
            // A delayed background save must never overwrite a newer shutdown/final save.
            if (version < _lastWrittenVersion) return;
            Directory.CreateDirectory(SettingsStore.DataDir);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StorePath, true);
            _lastWrittenVersion = version;
        }
    }

    static void LogPersistenceError(string operation, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DataDir);
            File.AppendAllText(Path.Combine(SettingsStore.DataDir, "download-manager.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Persistence {operation} failed: {ex}{Environment.NewLine}");
        }
        catch { /* Logging must never crash the UI. */ }
    }

    void Hook(DownloadItem it) => it.PropertyChanged += OnItemPropertyChanged;

    void OnItemPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (s is not DownloadItem it) return;
        ItemChanged?.Invoke(it, e.PropertyName ?? "");
        // Only child download state changes affect the aggregate playlist summary.
        // UpdatePlaylistSummary raises its own Display* notifications; reacting to those
        // notifications here would recursively call UpdatePlaylistSummary until the app crashes.
        if (it.IsPlaylist && e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.Progress))
        {
            var children = Items.Where(x => x.IsPlaylist && x.PlaylistGroupId == it.PlaylistGroupId).ToList();
            children.FirstOrDefault(x => x.IsPlaylistRepresentative)?.UpdatePlaylistSummary(children);
        }
        if (e.PropertyName == nameof(DownloadItem.Status)) { ScheduleSave(); Changed?.Invoke(); }
        else if (e.PropertyName == nameof(DownloadItem.Progress)) ScheduleSave();
    }

    // ------------------------------------------------------------ commands
    public void Add(IEnumerable<DownloadItem> items, bool startNow)
    {
        foreach (var it in items)
        {
            it.Status = DownloadStatus.Queued;
            it.AddedAt = DateTime.Now;
            Hook(it);
            Items.Add(it);
            // Both immediate downloads and queued downloads respect the concurrency limit.
            it.WantsStart = startNow || S.AutoStartQueue;
        }
        ScheduleSave();
        Changed?.Invoke();
        Pump();
    }

    /// <summary>Starts waiting items while there are free slots.</summary>
    public void Pump()
    {
        int running = _running.Count;
        foreach (var it in Items.ToList())
        {
            if (running >= Math.Max(1, S.MaxConcurrent)) break;
            if (it.Status == DownloadStatus.Queued && it.WantsStart && !_running.ContainsKey(it.Id))
            {
                Run(it);
                running++;
            }
        }
    }

    public async void Run(DownloadItem it)
    {
        if (_running.ContainsKey(it.Id))
        {
            // The previous attempt is still unwinding; let Pump restart it when it exits.
            it.WantsStart = true;
            it.Status = DownloadStatus.Queued;
            return;
        }

        // Resume and retry must obey the same concurrency cap as newly added items.
        if (_running.Count >= Math.Max(1, S.MaxConcurrent))
        {
            it.WantsStart = true;
            it.Status = DownloadStatus.Queued;
            return;
        }

        var cts = new CancellationTokenSource();
        _running[it.Id] = cts;
        it.Error = "";
        // Preserve the last visible percentage when resuming a paused download.
        it.Status = DownloadStatus.Downloading;
        // Yield before any engine/tool preparation so Start Download never blocks the WPF UI thread.
        await Task.Yield();
        try
        {
            // Keep the async engine on the WPF synchronization context. Its progress callbacks
            // update bound DownloadItem properties; moving the whole engine to Task.Run causes
            // WPF DependencySource cross-thread exceptions and can terminate the app.
            // Retry transient network/CDN failures automatically, but do not waste time repeating
            // permanent errors such as private/deleted videos or unsupported URLs.
            const int maxTransientRetries = 2;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await DownloadEngine.RunAsync(it, S, cts.Token);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (attempt < maxTransientRetries && IsTransientDownloadError(ex))
                {
                    it.Error = $"Temporary connection problem. Retrying ({attempt + 1}/{maxTransientRetries})…";
                    await Task.Delay(TimeSpan.FromSeconds(2 + attempt * 3), cts.Token);
                }
            }
            it.Progress = 100;
            it.FinishedAt = DateTime.Now;
            it.Status = DownloadStatus.Completed;
            if (!it.IsPlaylist)
            {
                DownloadCompleted?.Invoke(it);
            }
            else
            {
                var group = Items.Where(x => x.IsPlaylist && x.PlaylistGroupId == it.PlaylistGroupId).ToList();
                if (group.Count > 0 && group.All(x => x.Status == DownloadStatus.Completed)
                    && !string.IsNullOrWhiteSpace(it.PlaylistGroupId)
                    && _notifiedPlaylistGroups.Add(it.PlaylistGroupId))
                {
                    var representative = group.FirstOrDefault(x => x.IsPlaylistRepresentative) ?? it;
                    DownloadCompleted?.Invoke(representative);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (it.Status == DownloadStatus.Downloading) it.Status = DownloadStatus.Paused;
        }
        catch (Exception ex)
        {
            it.Error = ex.GetBaseException().Message;
            try
            {
                var logDir = Path.Combine(SettingsStore.DataDir, "logs");
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, "download-errors.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {it.Title} ({it.Url}){Environment.NewLine}{ex}{Environment.NewLine}---{Environment.NewLine}");
            }
            catch { }
            it.FinishedAt = DateTime.Now;
            it.Status = DownloadStatus.Failed;
        }
        finally
        {
            _running.Remove(it.Id);
            DownloadEngine.ClearProgressState(it.Id);
            cts.Dispose();
            ScheduleSave();
            Pump();
        }
    }

    static bool IsTransientDownloadError(Exception ex)
    {
        var message = ex.GetBaseException().Message.ToLowerInvariant();
        return new[]
        {
            "http error 403", "http error 408", "http error 429", "http error 500", "http error 502",
            "http error 503", "http error 504", "timed out", "timeout", "connection reset",
            "connection refused", "connection aborted", "temporary failure", "temporarily unavailable",
            "network is unreachable", "unable to download webpage", "remote end closed", "tls",
            "incomplete read", "fragment", "server returned"
        }.Any(pattern => message.Contains(pattern, StringComparison.Ordinal));
    }

    public void Pause(DownloadItem it)
    {
        it.WantsStart = false;
        if (it.Status is DownloadStatus.Downloading or DownloadStatus.Queued)
        {
            it.Status = DownloadStatus.Paused;
            if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
        }
    }

    public void Resume(DownloadItem it)   // Paused / Queued / Failed → schedule within concurrency limit
    {
        if (it.Status == DownloadStatus.Completed) return;
        it.WantsStart = true;
        it.Error = "";
        it.Status = DownloadStatus.Queued;
        Pump();
    }

    public void PauseAll() { foreach (var i in Items.ToList()) Pause(i); }

    public void ResumeAll()
    {
        foreach (var i in Items.Where(x => x.Status is DownloadStatus.Paused or DownloadStatus.Queued).ToList())
        {
            i.WantsStart = true;
            i.Status = DownloadStatus.Queued;
        }
        Pump();
    }

    public void Retry(DownloadItem it)
    {
        if (_running.ContainsKey(it.Id)) return;
        // Keep the last visible progress when retrying so the UI does not jump backwards.
        it.Error = "";
        it.WantsStart = true;
        it.Status = DownloadStatus.Queued;
        Pump();
    }

    public void Remove(IEnumerable<DownloadItem> items, bool deleteFiles)
    {
        foreach (var it in items.ToList())
        {
            it.WantsStart = false;
            if (_running.TryGetValue(it.Id, out var c)) c.Cancel();
            if (deleteFiles && !string.IsNullOrEmpty(it.FilePath))
                try { if (File.Exists(it.FilePath)) File.Delete(it.FilePath); } catch { }
            it.PropertyChanged -= OnItemPropertyChanged;
            Items.Remove(it);
        }
        ScheduleSave();
        Changed?.Invoke();
    }

    public void Shutdown()
    {
        _saveTimer.Stop();
        foreach (var c in _running.Values) c.Cancel();
        foreach (var i in Items) if (i.Status is DownloadStatus.Downloading or DownloadStatus.Queued) i.Status = DownloadStatus.Paused;
        Save();
    }
}
