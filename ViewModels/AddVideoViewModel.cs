using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public partial class QualityOption : ObservableObject
{
    public string Label { get; init; } = "";
    public int Height { get; init; }
    public int Kbps { get; init; }
    [ObservableProperty] private long _sizeBytes;
    partial void OnSizeBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
    public bool IsAvailable { get; init; } = true;
    public bool ShowAvailability { get; init; }
    public string SizeText => SizeBytes > 0 ? "~ " + Fmt.Bytes(SizeBytes) : "—";
    [ObservableProperty] private bool _isSelected;
    public override string ToString() => Label;
}

public partial class PlaylistEntryVm : ObservableObject
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Channel { get; init; } = "";
    public string DurationText { get; init; } = "";
    public string Thumb { get; init; } = "";
    public TimeSpan? Duration { get; init; }
    public Dictionary<int, long> QualitySizes { get; init; } = new();
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _sizeText = "—";
}

public enum LinkState { Idle, Loading, Video, Playlist, Error }

public partial class AddVideoViewModel : ObservableObject
{
    static readonly (int h, string tag)[] Heights =
    {
        (4320, "8K"), (2160, "4K"), (1440, "2K"), (1080, "Full HD"), (720, "HD"),
        (480, ""), (360, ""), (240, ""), (144, "")
    };
    static readonly int[] Bitrates = { 320, 256, 192, 128 };

    readonly DownloadManager _mgr;
    ResolveResult? _res;
    string _lastUrl = "";
    CancellationTokenSource? _cts;

    public Action? Done { get; set; }                  // go to the Downloads screen
    public Func<string?>? PickFolder { get; set; }
    // true = download with a new name, false = skip, null = cancel the whole action
    public Func<string, bool?>? DuplicateFilePrompt { get; set; }
    // true = playlist, false = only the linked video, null = leave the preview without changing mode
    public Func<string, bool?>? PlaylistLinkPrompt { get; set; }

    public ObservableCollection<QualityOption> VideoQualities { get; } = new();
    public ObservableCollection<QualityOption> AudioQualities { get; } = new();
    public ObservableCollection<PlaylistEntryVm> Entries { get; } = new();
    public ObservableCollection<MediaTrackOption> AudioTracks { get; } = new();
    public ObservableCollection<MediaTrackOption> OriginalSubtitleTracks { get; } = new();
    public ObservableCollection<MediaTrackOption> AutoTranslatedSubtitleTracks { get; } = new();
    public ObservableCollection<string> VideoFormats { get; } = new() { "MP4", "MKV" };
    public ObservableCollection<string> AudioFormats { get; } = new() { "MP3", "M4A" };

    [ObservableProperty] private string _selectedVideoFormat = "MP4";
    [ObservableProperty] private string _selectedAudioFormat = "MP3";

    [ObservableProperty] private string _url = "";
    [ObservableProperty] private LinkState _state = LinkState.Idle;
    [ObservableProperty] private string _errorText = "";

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private string _thumb = "";
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private string _viewsText = "";
    [ObservableProperty] private string _publishedText = "";
    [ObservableProperty] private string _playlistInfo = "";
    [ObservableProperty] private bool _hasSingleVideoOption;

    [ObservableProperty] private bool _isAudio;
    [ObservableProperty] private QualityOption? _selectedVideoQuality;
    [ObservableProperty] private QualityOption? _selectedAudioQuality;
    [ObservableProperty] private MediaTrackOption? _selectedAudioTrack;
    [ObservableProperty] private MediaTrackOption? _selectedOriginalSubtitleTrack;
    [ObservableProperty] private MediaTrackOption? _selectedAutoTranslatedSubtitleTrack;

    [ObservableProperty] private string _saveFolder = "";
    [ObservableProperty] private bool _createSubfolder;
    [ObservableProperty] private bool _numberItems;
    [ObservableProperty] private bool _createM3U;
    [ObservableProperty] private bool _includeChapters;
    [ObservableProperty] private bool _addThumbnail;
    [ObservableProperty] private bool _allSelected = true;
    [ObservableProperty] private int _selectedEntries;

    public bool IsLoading => State == LinkState.Loading;
    public bool HasError => State == LinkState.Error;
    public bool IsVideoMode => State == LinkState.Video;
    public bool IsPlaylistMode => State == LinkState.Playlist;
    public bool IsSingleVideoMode => IsVideoMode && !IsPlaylistMode;
    public bool HasContent => IsVideoMode || IsPlaylistMode;
    public bool IsVideo { get => !IsAudio; set { if (value) IsAudio = false; } }
    public bool CanDownload => HasContent
        && (IsAudio ? SelectedAudioQuality != null : SelectedVideoQuality != null)
        && (!IsPlaylistMode || SelectedEntries > 0);
    public bool CanDownloadAgain => IsPlaylistMode && Entries.Count > 0
        && (IsAudio ? SelectedAudioQuality != null : SelectedVideoQuality != null);
    public string SelectedCountText => $"{SelectedEntries} / {Entries.Count} {Loc.T("S.Selected")}";

    public string TotalSizeText
    {
        get
        {
            if (!HasContent) return "—";
            if (IsAudio && SelectedAudioQuality != null)
            {
                double secs = IsPlaylistMode
                    ? Entries.Where(e => e.IsSelected).Sum(e => e.Duration?.TotalSeconds ?? 0)
                    : _res?.Duration?.TotalSeconds ?? 0;
                return secs > 0 ? "~ " + Fmt.Bytes((long)(secs * SelectedAudioQuality.Kbps * 125)) : "—";
            }
            if (!IsAudio && SelectedVideoQuality != null)
            {
                if (IsPlaylistMode)
                {
                    long selectedTotal = EstimateVideoBytes(null, SelectedVideoQuality.Height);
                    return selectedTotal > 0 ? "~ " + Fmt.Bytes(selectedTotal) : "—";
                }
                return SelectedVideoQuality.SizeText;
            }
            return "—";
        }
    }

    public string TotalSizeFor
    {
        get
        {
            var q = IsAudio ? SelectedAudioQuality?.Label : SelectedVideoQuality?.Label;
            if (q == null) return "";
            var kind = IsAudio ? SelectedAudioFormat : SelectedVideoFormat;
            return IsPlaylistMode ? $"({SelectedEntries} {Loc.T("S.Videos")} • {q} {kind})" : $"({Loc.T("S.For")} {q} {kind})";
        }
    }

    public AddVideoViewModel(DownloadManager mgr)
    {
        _mgr = mgr;
        ApplyDefaults();
        Loc.Changed += () => { OnPropertyChanged(nameof(SelectedCountText)); OnPropertyChanged(nameof(TotalSizeFor)); };
        SettingsStore.Changed += () => { if (!HasContent) ApplyDefaults(); else SaveFolder = IsAudio ? SettingsStore.Current.AudioDownloadFolder : SettingsStore.Current.DownloadFolder; };
    }

    void ApplyDefaults()
    {
        var s = SettingsStore.Current;
        SaveFolder = IsAudio ? s.AudioDownloadFolder : s.DownloadFolder;
        CreateSubfolder = s.CreatePlaylistSubfolder;
        NumberItems = s.NumberPlaylistItems;
        IncludeChapters = s.IncludeChaptersByDefault || (_res?.Tracks.HasChapters ?? false);
        AddThumbnail = true;
        IsAudio = s.DefaultIsAudio;
    }

    // ------------------------------------------------------------ link handling
    partial void OnUrlChanged(string value)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (string.IsNullOrWhiteSpace(value)) { Reset(keepUrl: true); return; }
        _ = ResolveDebounced(value.Trim(), false, ct);
    }

    async Task ResolveDebounced(string url, bool single, CancellationToken ct)
    {
        try { await Task.Delay(550, ct); } catch { return; }
        await ResolveAsync(url, single, ct);
    }

    async Task ResolveAsync(string url, bool single, CancellationToken ct)
    {
        _lastUrl = url;
        if (!YouTubeService.LooksLikeYouTube(url))
        {
            ErrorText = Loc.T("S.ErrInvalid");
            State = LinkState.Error;
            return;
        }
        ErrorText = "";
        bool resolveSingle = single;
        if (!single && YouTubeService.HasVideoAndPlaylist(url) && PlaylistLinkPrompt != null)
        {
            var choice = PlaylistLinkPrompt("");
            if (ct.IsCancellationRequested) return;
            if (choice is null)
            {
                State = LinkState.Idle;
                return;
            }
            resolveSingle = choice == false;
        }

        State = LinkState.Loading;
        try
        {
            var r = await YouTubeService.ResolveAsync(url, resolveSingle, ct);
            if (ct.IsCancellationRequested) return;
            Apply(r);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            ErrorText = Loc.T("S.ErrFetch") + " (" + ex.Message + ")";
            State = LinkState.Error;
        }
    }

    [RelayCommand]
    async Task GetVideo()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        await ResolveAsync(Url.Trim(), false, _cts.Token);
    }

    [RelayCommand]
    async Task OnlyThisVideo()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        await ResolveAsync(_lastUrl, true, _cts.Token);
    }

    void Apply(ResolveResult r)
    {
        _res = r;
        IncludeChapters = !IsAudio && (SettingsStore.Current.IncludeChaptersByDefault || r.Tracks.HasChapters);
        Title = r.Title;
        Channel = r.Channel;
        Thumb = r.Thumb;
        DurationText = Fmt.Duration(r.Duration);
        HasSingleVideoOption = r.IsPlaylist && r.SingleVideoId != null;

        Entries.Clear();
        if (r.IsPlaylist)
        {
            foreach (var e in r.Entries)
            {
                var vm = new PlaylistEntryVm
                {
                    Index = e.Index, Id = e.Id, Title = e.Title, Channel = e.Channel, Thumb = e.Thumb,
                    Duration = e.Duration, DurationText = Fmt.Duration(e.Duration),
                    QualitySizes = e.VideoFormats.ToDictionary(f => f.Height, f => f.EstimatedBytes)
                };
                vm.PropertyChanged += (_, ev) => { if (ev.PropertyName == nameof(PlaylistEntryVm.IsSelected)) RecountEntries(); };
                // Existing downloads should be visible in the playlist preview but excluded by default.
                vm.IsSelected = !IsEntryAlreadyDownloaded(vm, r.PlaylistTitle);
                Entries.Add(vm);
            }
            PlaylistInfo = $"{r.Entries.Count} {Loc.T("S.Videos")} • {Fmt.Duration(r.Duration)}";
            ViewsText = ""; PublishedText = "";
        }
        else
        {
            ViewsText = $"{Fmt.Views(r.Views)} {Loc.T("S.ViewsWord")}";
            PublishedText = r.Published?.ToString("MMM d, yyyy") ?? "";
        }

        AudioTracks.Clear();
        foreach (var track in r.Tracks.AudioTracks) AudioTracks.Add(track);
        OriginalSubtitleTracks.Clear();
        AutoTranslatedSubtitleTracks.Clear();
        OriginalSubtitleTracks.Add(new MediaTrackOption { Id = "none", Language = "none", Label = "No Subtitles" });
        AutoTranslatedSubtitleTracks.Add(new MediaTrackOption { Id = "none", Language = "none", Label = "No Subtitles" });
        foreach (var track in r.Tracks.SubtitleTracks)
        {
            if (track.Id.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)) AutoTranslatedSubtitleTracks.Add(track);
            else if (track.Id.StartsWith("original:", StringComparison.OrdinalIgnoreCase)) OriginalSubtitleTracks.Add(track);
        }
        SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.Id == "default") ?? AudioTracks.FirstOrDefault();
        SelectedOriginalSubtitleTrack = OriginalSubtitleTracks.FirstOrDefault(t => t.Id == "none");
        SelectedAutoTranslatedSubtitleTrack = AutoTranslatedSubtitleTracks.FirstOrDefault(t => t.Id == "none");

        BuildQualities(r);
        UpdateEntrySizeEstimates();
        _allSelectedUpdating = true; AllSelected = Entries.Count > 0 && Entries.All(e => e.IsSelected); _allSelectedUpdating = false;
        RecountEntries();
        State = r.IsPlaylist ? LinkState.Playlist : LinkState.Video;
    }


    bool IsEntryAlreadyDownloaded(PlaylistEntryVm entry, string playlistTitle)
    {
        try
        {
            string root = SaveFolder;
            if (string.IsNullOrWhiteSpace(root)) root = IsAudio ? WindowsLibraryPaths.AudioFolder : WindowsLibraryPaths.VideoFolder;
            string folder = CreateSubfolder ? Path.Combine(root, Fmt.Sanitize(playlistTitle)) : root;

            // Determine duplicates from files in the actual destination folder only; History is not authoritative.
            if (!Directory.Exists(folder)) return false;
            string stem = Fmt.Sanitize(entry.Title);
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".opus", ".aac" };
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (!extensions.Contains(Path.GetExtension(path))) continue;
                string fileStem = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileStem, stem, StringComparison.OrdinalIgnoreCase)) return true;
                // Match a prior numbered name such as "01 - Video title" even if the option changed.
                int separator = fileStem.IndexOf(" - ", StringComparison.Ordinal);
                if (separator > 0 && int.TryParse(fileStem[..separator], out _) &&
                    string.Equals(fileStem[(separator + 3)..], stem, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { /* A failed filesystem probe should not prevent adding the playlist. */ }
        return false;
    }

    void BuildQualities(ResolveResult r)
    {
        VideoQualities.Clear();
        AudioQualities.Clear();
        var m = r.Manifest;

        foreach (var (h, tag) in Heights)
        {
            long size = 0;
            bool available;
            int supportedCount = 0;
            if (r.IsPlaylist)
            {
                var supported = Entries.Where(e => e.QualitySizes.ContainsKey(h)).ToList();
                supportedCount = supported.Count;
                available = supportedCount > 0;
                // Never show a fallback/lower-resolution estimate for a quality the playlist does not contain.
                size = available
                    ? Entries.Where(e => e.IsSelected).Sum(e => EstimateEntrySize(e, h))
                    : 0;
            }
            else
            {
                var v = StreamPicker.Exact(m, h);
                available = v != null;
                if (v != null)
                {
                    var a = StreamPicker.Audio(m, v.Container.Name == "mp4");
                    size = v.Size.Bytes + (a?.Size.Bytes ?? 0);
                }
            }
            string label = tag == "" ? $"{h}p" : $"{h}p ({tag})";
            if (r.IsPlaylist) label += $" — {supportedCount}/{Entries.Count}";
            var opt = new QualityOption
            {
                Label = label, Height = h, SizeBytes = size,
                IsAvailable = available, ShowAvailability = true
            };
            opt.PropertyChanged += (_, e) => OnQualityPicked(VideoQualities, opt, e.PropertyName, o => SelectedVideoQuality = o);
            VideoQualities.Add(opt);
        }

        foreach (var k in Bitrates)
        {
            double seconds = r.IsPlaylist
                ? Entries.Where(e => e.IsSelected).Sum(e => e.Duration?.TotalSeconds ?? 0)
                : (r.Duration?.TotalSeconds ?? 0);
            long size = (long)(seconds * k * 125);
            var opt = new QualityOption { Label = $"{k} kbps", Kbps = k, SizeBytes = size };
            opt.PropertyChanged += (_, e) => OnQualityPicked(AudioQualities, opt, e.PropertyName, o => SelectedAudioQuality = o);
            AudioQualities.Add(opt);
        }

        var s = SettingsStore.Current;
        var pickV = VideoQualities.Where(q => q.IsAvailable && q.Height <= s.DefaultVideoHeight).FirstOrDefault()
                    ?? VideoQualities.FirstOrDefault(q => q.IsAvailable);
        SelectedVideoQuality = null;
        if (pickV != null) pickV.IsSelected = true;

        var pickA = AudioQualities.FirstOrDefault(q => q.Kbps == s.DefaultAudioKbps) ?? AudioQualities.First();
        SelectedAudioQuality = null;
        pickA.IsSelected = true;
    }

    static void OnQualityPicked(ObservableCollection<QualityOption> list, QualityOption opt, string? prop, Action<QualityOption> set)
    {
        if (prop != nameof(QualityOption.IsSelected) || !opt.IsSelected) return;
        foreach (var o in list.Where(o => o != opt && o.IsSelected)) o.IsSelected = false;
        set(opt);
    }

    // ------------------------------------------------------------ change notifications
    partial void OnStateChanged(LinkState value)
    {
        foreach (var n in new[] { nameof(IsLoading), nameof(HasError), nameof(IsVideoMode), nameof(IsPlaylistMode), nameof(IsSingleVideoMode), nameof(HasContent), nameof(CanDownloadAgain) })
            OnPropertyChanged(n);
        Totals();
    }
    partial void OnIsAudioChanged(bool value)
    {
        SaveFolder = value ? SettingsStore.Current.AudioDownloadFolder : SettingsStore.Current.DownloadFolder;
        OnPropertyChanged(nameof(IsVideo));
        Totals();
    }
    partial void OnSelectedVideoQualityChanged(QualityOption? value) { OnPropertyChanged(nameof(CanDownloadAgain)); Totals(); }
    partial void OnSelectedAudioQualityChanged(QualityOption? value) { OnPropertyChanged(nameof(CanDownloadAgain)); Totals(); }
    partial void OnSelectedVideoFormatChanged(string value) { OnPropertyChanged(nameof(TotalSizeFor)); }
    partial void OnSelectedAudioFormatChanged(string value) { OnPropertyChanged(nameof(TotalSizeFor)); }

    long EstimateVideoBytes(TimeSpan? duration, int height, PlaylistEntryVm? entry = null)
    {
        if (_res?.IsPlaylist == true)
            return entry != null ? EstimateEntrySize(entry, height) :
                Entries.Where(e => e.IsSelected).Sum(e => EstimateEntrySize(e, height));
        if (duration is not TimeSpan d || d.TotalSeconds <= 0 || _res?.Manifest == null) return 0;
        var video = StreamPicker.Exact(_res.Manifest, height);
        if (video == null) return 0;
        var audio = StreamPicker.Audio(_res.Manifest, video.Container.Name == "mp4");
        long known = video.Size.Bytes + (audio?.Size.Bytes ?? 0);
        if (known > 0)
        {
            var firstDuration = _res.Entries.FirstOrDefault()?.Duration?.TotalSeconds ?? _res.Duration?.TotalSeconds ?? 0;
            if (firstDuration > 0) return (long)(known / firstDuration * d.TotalSeconds);
        }
        // Some YouTube manifests omit content length. Fall back to stream bitrates.
        long bitsPerSecond = video.Bitrate.BitsPerSecond + (audio?.Bitrate.BitsPerSecond ?? 0);
        return bitsPerSecond > 0 ? (long)(bitsPerSecond / 8d * d.TotalSeconds) : 0;
    }

    static long EstimateEntrySize(PlaylistEntryVm entry, int targetHeight)
    {
        if (entry.QualitySizes.Count == 0) return 0;
        int selectedHeight = entry.QualitySizes.Keys.Where(h => h <= targetHeight)
            .DefaultIfEmpty(entry.QualitySizes.Keys.Min()).Max();
        return entry.QualitySizes.TryGetValue(selectedHeight, out long bytes) ? bytes : 0;
    }

    void Totals()
    {
        // Refresh playlist quality-size estimates when the selection changes.
        if (_res?.IsPlaylist == true)
        {
            foreach (var q in VideoQualities)
                q.SizeBytes = Entries.Where(e => e.IsSelected).Sum(e => EstimateEntrySize(e, q.Height));
            var selectedSeconds = Entries.Where(e => e.IsSelected).Sum(e => e.Duration?.TotalSeconds ?? 0);
            foreach (var q in AudioQualities)
                q.SizeBytes = (long)(selectedSeconds * q.Kbps * 125);
        }
        UpdateEntrySizeEstimates();
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(TotalSizeFor));
        OnPropertyChanged(nameof(CanDownload));
    }

    void UpdateEntrySizeEstimates()
    {
        if (Entries.Count == 0) return;
        if (IsAudio && SelectedAudioQuality != null)
        {
            foreach (var e in Entries)
                e.SizeText = e.Duration is TimeSpan d
                    ? "~ " + Fmt.Bytes((long)(d.TotalSeconds * SelectedAudioQuality.Kbps * 125))
                    : "—";
            return;
        }
        if (SelectedVideoQuality == null)
        {
            foreach (var e in Entries) e.SizeText = "—";
            return;
        }
        foreach (var e in Entries)
        {
            long bytes = EstimateEntrySize(e, SelectedVideoQuality.Height);
            e.SizeText = bytes > 0 ? "~ " + Fmt.Bytes(bytes) : "Unavailable";
        }
    }

    bool _allSelectedUpdating;
    void RecountEntries()
    {
        SelectedEntries = Entries.Count(e => e.IsSelected);
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(TotalSizeFor));
        _allSelectedUpdating = true;
        AllSelected = Entries.Count > 0 && SelectedEntries == Entries.Count;
        _allSelectedUpdating = false;
        Totals();
    }
    partial void OnAllSelectedChanged(bool value)
    {
        if (_allSelectedUpdating) return;
        foreach (var e in Entries) e.IsSelected = value;
    }

    // ------------------------------------------------------------ actions
    void Reset(bool keepUrl)
    {
        _res = null;
        Entries.Clear(); VideoQualities.Clear(); AudioQualities.Clear(); AudioTracks.Clear(); OriginalSubtitleTracks.Clear(); AutoTranslatedSubtitleTracks.Clear();
        SelectedVideoQuality = null; SelectedAudioQuality = null; SelectedAudioTrack = null; SelectedOriginalSubtitleTrack = null; SelectedAutoTranslatedSubtitleTrack = null;
        ErrorText = "";
        State = LinkState.Idle;
        if (!keepUrl) Url = "";
        ApplyDefaults();
    }

    [RelayCommand] void ClearUrl() => Url = "";
    [RelayCommand] void Cancel() => Url = "";

    [RelayCommand]
    void Browse()
    {
        var p = PickFolder?.Invoke();
        if (string.IsNullOrEmpty(p)) return;
        SaveFolder = p;
        if (IsAudio) SettingsStore.Current.AudioDownloadFolder = p;
        else SettingsStore.Current.DownloadFolder = p;
        SettingsStore.Save();
    }

    [RelayCommand] void AddToQueue() => Submit(false);
    [RelayCommand] void DownloadNow() => Submit(true);
    [RelayCommand] void DownloadAgain()
    {
        if (!CanDownloadAgain || _res == null) return;
        foreach (var entry in Entries) entry.IsSelected = true;
        RecountEntries();
        Submit(true, true);
    }

    void Submit(bool now, bool downloadAgain = false)
    {
        if ((!CanDownload && !downloadAgain) || _res == null) return;
        var items = BuildItems(downloadAgain);
        if (items.Count == 0)
        {
            ErrorText = "All selected videos are already downloaded in this destination.";
            return;
        }
        if (!_res!.IsPlaylist && IsDuplicate(items[0]))
        {
            var decision = DuplicateFilePrompt?.Invoke(items[0].Title);
            if (decision is null) return;
            if (decision == false) return;
            items[0].FileBaseName = NextAvailableBaseName(items[0].OutputFolder, items[0].FileBaseName, items[0].Format);
        }
        if (CreateM3U && _res?.IsPlaylist == true) WritePlaylistM3U(items);
        // Queue first, then navigate. All progress callbacks are marshalled to the UI dispatcher.
        Url = "";
        _mgr.Add(items, now, respectAutoStart: false);
        Done?.Invoke();
    }

    void WritePlaylistM3U(List<DownloadItem> items)
    {
        if (items.Count == 0 || _res == null) return;
        try
        {
            var folder = items[0].OutputFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Fmt.Sanitize(_res.PlaylistTitle) + ".m3u");
            var lines = new List<string> { "#EXTM3U" };
            foreach (var item in items)
            {
                var ext = item.IsAudio
                    ? (item.Format.Equals("M4A", StringComparison.OrdinalIgnoreCase) ? ".m4a" : ".mp3")
                    : (item.Format.Equals("MKV", StringComparison.OrdinalIgnoreCase) ? ".mkv" : ".mp4");
                var fileName = Fmt.Sanitize(item.FileBaseName) + ext;
                var entry = _res.Entries.FirstOrDefault(e => e.Id == item.VideoId);
                var duration = (int)Math.Round(entry?.Duration?.TotalSeconds ?? -1);
                lines.Add($"#EXTINF:{duration},{item.Title}");
                lines.Add(fileName);
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            ErrorText = "Could not create M3U playlist file: " + ex.Message;
            State = LinkState.Error;
        }
    }

    bool IsDuplicate(DownloadItem item)
    {
        if (_mgr.Items.Any(old => old.VideoId == item.VideoId && old.Status == DownloadStatus.Completed
            && string.Equals(Path.GetFullPath(old.OutputFolder), Path.GetFullPath(item.OutputFolder), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(old.FilePath) && File.Exists(old.FilePath))) return true;
        return HasExistingMediaFile(item.OutputFolder, item.FileBaseName);
    }

    static bool HasExistingMediaFile(string folder, string baseName)
    {
        if (!Directory.Exists(folder)) return false;
        var stem = Fmt.Sanitize(baseName);
        return Directory.EnumerateFiles(folder, stem + ".*", SearchOption.TopDirectoryOnly)
            .Any(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(".part.", StringComparison.OrdinalIgnoreCase)
                && new[] { ".mp4", ".mkv", ".webm", ".mp3", ".m4a", ".opus", ".aac" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    static string NextAvailableBaseName(string folder, string baseName, string format)
    {
        string stem = Fmt.Sanitize(baseName);
        for (int i = 2; i < 10000; i++)
        {
            string candidate = $"{stem} {i}";
            if (!HasExistingMediaFile(folder, candidate)) return candidate;
        }
        return stem + " " + Guid.NewGuid().ToString("N")[..6];
    }

    List<DownloadItem> BuildItems(bool downloadAgain = false)
    {
        int h = SelectedVideoQuality?.Height ?? 1080;
        int k = SelectedAudioQuality?.Kbps ?? 192;
        string details = IsAudio ? $"{SelectedAudioFormat} • {k}kbps" : $"{SelectedVideoFormat} • {h}p";
        var list = new List<DownloadItem>();
        var r = _res!;
        string playlistGroupId = r.IsPlaylist ? Guid.NewGuid().ToString("N") : "";
        string actualSaveFolder = SaveFolder;
        if (string.IsNullOrWhiteSpace(actualSaveFolder)) actualSaveFolder = IsAudio ? WindowsLibraryPaths.AudioFolder : WindowsLibraryPaths.VideoFolder;
        Directory.CreateDirectory(actualSaveFolder);

        DownloadItem Make(string id, string title, string folder, string baseName, long total) => new()
        {
            VideoId = id, Title = title, Thumbnail = YouTubeService.Thumb(id, "mqdefault"),
            Url = $"https://www.youtube.com/watch?v={id}",
            IsAudio = IsAudio, Height = h, Kbps = k, Format = IsAudio ? SelectedAudioFormat : SelectedVideoFormat, AddThumbnail = IsAudio,
            AudioTrackLanguage = SelectedAudioTrack?.Id ?? "default",
            SubtitleTrackLanguage = string.Join(";", new[] { SelectedOriginalSubtitleTrack?.Id, SelectedAutoTranslatedSubtitleTrack?.Id }
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != "none").Distinct(StringComparer.OrdinalIgnoreCase)),
            IncludeChapters = !IsAudio && IncludeChapters,
            OutputFolder = folder, FileBaseName = baseName, Details = details, TotalBytes = total,
            PlaylistGroupId = playlistGroupId, PlaylistTitle = r.IsPlaylist ? r.PlaylistTitle : "", PlaylistCount = r.IsPlaylist ? Entries.Count(e => e.IsSelected) : 0
        };

        if (!r.IsPlaylist)
        {
            long total = IsAudio ? (long)((r.Duration?.TotalSeconds ?? 0) * k * 125) : EstimateVideoBytes(r.Duration, h);
            if (total <= 0) total = SelectedVideoQuality?.SizeBytes ?? 0;
            list.Add(Make(r.VideoId, r.Title, actualSaveFolder, r.Title, total));
            return list;
        }

        string folder = CreateSubfolder ? Path.Combine(actualSaveFolder, Fmt.Sanitize(r.PlaylistTitle)) : actualSaveFolder;
        int width = Math.Max(2, Entries.Count.ToString().Length);
        int playlistIndex = 0;
        foreach (var e in Entries.Where(e => e.IsSelected))
        {
            // Do not queue a completed video again when its file still exists in this exact path.
            bool alreadyCompleted = _mgr.Items.Any(old => old.VideoId == e.Id && old.Status == DownloadStatus.Completed
                && string.Equals(Path.GetFullPath(old.OutputFolder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(old.FilePath) && File.Exists(old.FilePath));
            string proposedName = NumberItems ? $"{e.Index.ToString().PadLeft(width, '0')} - {e.Title}" : e.Title;
            bool fileExists = HasExistingMediaFile(folder, proposedName);
            if ((alreadyCompleted || fileExists) && !downloadAgain) continue;
            if (downloadAgain && (alreadyCompleted || fileExists))
                proposedName = NextAvailableBaseName(folder, proposedName, IsAudio ? SelectedAudioFormat : SelectedVideoFormat);
            playlistIndex++;
            long total = IsAudio ? (long)((e.Duration?.TotalSeconds ?? 0) * k * 125) : EstimateVideoBytes(e.Duration, h, e);
            var item = Make(e.Id, e.Title, folder, proposedName, total);
            item.PlaylistIndex = playlistIndex;
            item.PlaylistCount = Entries.Count(e => e.IsSelected);
            list.Add(item);
        }
        // Keep the representative playlist's title and item count in sync with what will be queued.
        foreach (var item in list) item.PlaylistCount = list.Count;
        return list;
    }
}
