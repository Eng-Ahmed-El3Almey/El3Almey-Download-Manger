using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public enum DownloadStatus { Downloading, Paused, Completed, Failed, Queued }

public partial class DownloadItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string VideoId { get; set; } = "";
    public bool IsAudio { get; set; }
    public int Height { get; set; } = 1080;
    public int Kbps { get; set; } = 192;
    public string Format { get; set; } = "MP4";
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
    public bool IsPlaylist => !string.IsNullOrWhiteSpace(PlaylistGroupId);
    public bool IsPlaylistRepresentative => IsPlaylist && PlaylistIndex == 1;
    public bool CanOpenPlaylist => IsPlaylistRepresentative;
    public bool CanShowPrimaryAction => !IsPlaylistRepresentative || Status == DownloadStatus.Downloading;
    public string PlaylistTotalSizeText => IsPlaylistRepresentative && PlaylistTotalBytes > 0 ? $"Total {Fmt.Bytes(PlaylistTotalBytes)}" : "";
    public long PlaylistTotalBytes { get; private set; }
    public string DisplayTitle => IsPlaylistRepresentative ? PlaylistTitle : (IsPlaylist && PlaylistIndex > 0 && !string.IsNullOrWhiteSpace(FileBaseName) ? FileBaseName : Title);
    public string PlaylistVideoDisplayTitle => IsPlaylist && PlaylistIndex > 0 && !string.IsNullOrWhiteSpace(FileBaseName) ? FileBaseName : Title;
    public string DisplayDetails => IsPlaylistRepresentative ? $"Playlist • {PlaylistCount} videos • {Details}" : (IsPlaylist && !string.Equals(FileBaseName, Title, StringComparison.Ordinal) ? $"{Details} • {FileBaseName}" : Details);
    double _displayProgress;
    string _displayStatusText = "";
    Brush _displayStatusFg = B("#4A9BFF");
    public double DisplayProgress => IsPlaylistRepresentative ? _displayProgress : Progress;
    public string DisplayProgressText => IsPlaylistRepresentative ? $"{DisplayProgress:0}% • {PlaylistCompletedCount}/{PlaylistCount}" : ProgressText;
    public int PlaylistCompletedCount { get; private set; }
    public int PlaylistFailedCount { get; private set; }
    public string DisplayStatusText => IsPlaylistRepresentative ? _displayStatusText : StatusText;
    public Brush DisplayStatusFg => IsPlaylistRepresentative ? _displayStatusFg : StatusFg;

    public void UpdatePlaylistSummary(IReadOnlyCollection<DownloadItem> children)
    {
        if (!IsPlaylistRepresentative || children.Count == 0) return;
        _displayProgress = children.Average(x => x.Progress);
        PlaylistCompletedCount = children.Count(x => x.Status == DownloadStatus.Completed);
        PlaylistFailedCount = children.Count(x => x.Status == DownloadStatus.Failed);
        PlaylistTotalBytes = children.Sum(x => Math.Max(0, x.TotalBytes));
        _displayStatusText = children.All(x => x.Status == DownloadStatus.Completed) ? Loc.T("S.St.Completed")
            : children.Any(x => x.Status == DownloadStatus.Downloading) ? Loc.T("S.St.Downloading")
            : children.Any(x => x.Status == DownloadStatus.Failed) ? Loc.T("S.St.Failed")
            : children.Any(x => x.Status == DownloadStatus.Queued) ? Loc.T("S.St.Queued")
            : Loc.T("S.St.Paused");
        _displayStatusFg = children.All(x => x.Status == DownloadStatus.Completed) ? B("#2FD27A")
            : children.Any(x => x.Status == DownloadStatus.Downloading) ? B("#4A9BFF")
            : children.Any(x => x.Status == DownloadStatus.Failed) ? B("#FF5468")
            : children.Any(x => x.Status == DownloadStatus.Queued) ? B("#A78BFA") : B("#F5A524");
        OnPropertyChanged(nameof(DisplayProgress)); OnPropertyChanged(nameof(DisplayProgressText));
        OnPropertyChanged(nameof(DisplayStatusText)); OnPropertyChanged(nameof(DisplayStatusFg));
        OnPropertyChanged(nameof(PlaylistTotalSizeText)); OnPropertyChanged(nameof(HistorySizeText)); OnPropertyChanged(nameof(CanOpenPlaylist));
        OnPropertyChanged(nameof(CanRetry)); OnPropertyChanged(nameof(CanRetryPlaylist));
    }
    public bool WantsStart { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _thumbnail;
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private DownloadStatus _status;
    [ObservableProperty] private DateTime? _finishedAt;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isMenuOpen;

    public string StatusText => Loc.T("S.St." + Status);
    public string ProgressText => $"{Progress:0}%";
    public string SizeText => TotalBytes <= 0 ? "—"
        : $"{Fmt.Bytes((long)(TotalBytes * Progress / 100.0))} / {Fmt.Bytes(TotalBytes)}";
    public string TotalSizeText => TotalBytes <= 0 ? "—" : Fmt.Bytes(TotalBytes);
    public string HistorySizeText => IsPlaylistRepresentative && PlaylistTotalBytes > 0 ? Fmt.Bytes(PlaylistTotalBytes) : TotalSizeText;
    public string DateText => (FinishedAt ?? AddedAt).ToString("g");
    public bool CanOpenFolder => Status != DownloadStatus.Failed;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    // Per-video Retry appears only on the exact failed video. The representative row gets
    // a separate playlist-level action when any child has failed.
    public bool CanRetry => Status == DownloadStatus.Failed;
    public bool CanRetryPlaylist => IsPlaylistRepresentative && PlaylistFailedCount > 0;
    public string PrimaryActionTooltip => IsPlaylistRepresentative
        ? (Status == DownloadStatus.Downloading ? "Pause playlist" : "Start / resume playlist")
        : Status switch
        {
            DownloadStatus.Downloading => "Pause download",
            DownloadStatus.Completed => "Play file",
            DownloadStatus.Failed => "Retry download",
            DownloadStatus.Paused => "Resume download",
            _ => "Start download"
        };

    public string PrimaryIcon => Status switch
    {
        DownloadStatus.Downloading => "\uE769",   // pause
        DownloadStatus.Failed => "\uE72C",        // retry
        _ => "\uE768"                             // play
    };

    static Brush B(string hex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        // WPF Freezables created/queried by background download callbacks must be frozen
        // before they can safely cross dispatcher/thread boundaries.
        if (brush.CanFreeze && !brush.IsFrozen) brush.Freeze();
        return brush;
    }
    public Brush StatusFg => Status switch
    {
        DownloadStatus.Downloading => B("#4A9BFF"),
        DownloadStatus.Completed => B("#2FD27A"),
        DownloadStatus.Paused => B("#F5A524"),
        DownloadStatus.Failed => B("#FF5468"),
        _ => B("#A78BFA")
    };
    public Brush StatusBg => Status switch
    {
        DownloadStatus.Downloading => B("#14325F"),
        DownloadStatus.Completed => B("#0F3A2A"),
        DownloadStatus.Paused => B("#3D2A10"),
        DownloadStatus.Failed => B("#431A25"),
        _ => B("#2A1F57")
    };

    public void RefreshLanguage() => OnPropertyChanged(nameof(StatusText));

    partial void OnTitleChanged(string value) { OnPropertyChanged(nameof(DisplayTitle)); OnPropertyChanged(nameof(PlaylistVideoDisplayTitle)); }
    partial void OnDetailsChanged(string value) => OnPropertyChanged(nameof(DisplayDetails));
    partial void OnStatusChanged(DownloadStatus value)
    {
        foreach (var n in new[] { nameof(StatusText), nameof(StatusFg), nameof(StatusBg), nameof(PrimaryIcon),
                                  nameof(CanOpenFolder), nameof(IsCompleted), nameof(CanRetry), nameof(CanRetryPlaylist), nameof(CanShowPrimaryAction), nameof(DisplayStatusText), nameof(DisplayStatusFg), nameof(PrimaryActionTooltip) })
            OnPropertyChanged(n);
    }
    partial void OnProgressChanged(double value) { OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(DisplayProgress)); OnPropertyChanged(nameof(DisplayProgressText)); }
    partial void OnTotalBytesChanged(long value) { OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(TotalSizeText)); }
    partial void OnFinishedAtChanged(DateTime? value) => OnPropertyChanged(nameof(DateText));
}
