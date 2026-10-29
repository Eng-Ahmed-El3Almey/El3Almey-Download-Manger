using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using El3Almey.Services;

namespace El3Almey.ViewModels;

public class LanguageOption
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

public class QualityChoice
{
    public int Value { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}

public partial class SettingsViewModel : ObservableObject
{
    readonly bool _loading;
    static AppSettings S => SettingsStore.Current;

    public LanguageOption[] Languages { get; } =
    {
        new() { Code = "en", Name = "English" },
        new() { Code = "ar", Name = "العربية" }
    };
    public QualityChoice[] VideoChoices { get; } =
    {
        new() { Value = 4320, Label = "4320p (8K)" }, new() { Value = 2160, Label = "2160p (4K)" },
        new() { Value = 1440, Label = "1440p (2K)" }, new() { Value = 1080, Label = "1080p (Full HD)" },
        new() { Value = 720, Label = "720p (HD)" }, new() { Value = 480, Label = "480p" },
        new() { Value = 360, Label = "360p" }
    };
    public QualityChoice[] AudioChoices { get; } =
    {
        new() { Value = 320, Label = "320 kbps" }, new() { Value = 256, Label = "256 kbps" },
        new() { Value = 192, Label = "192 kbps" }, new() { Value = 128, Label = "128 kbps" }
    };
    public int[] ConcurrentChoices { get; } = { 1, 2, 3, 4, 5, 6 };

    [ObservableProperty] private LanguageOption _selectedLanguage;
    [ObservableProperty] private string _downloadFolder;
    [ObservableProperty] private string _audioDownloadFolder;
    [ObservableProperty] private bool _defaultIsAudio;
    [ObservableProperty] private QualityChoice _defaultVideo;
    [ObservableProperty] private QualityChoice _defaultAudio;
    [ObservableProperty] private bool _createSubfolder;
    [ObservableProperty] private bool _numberItems;
    [ObservableProperty] private bool _includeChaptersByDefault;
    [ObservableProperty] private int _maxConcurrent;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _ffmpegPath;

    public Func<string?>? PickFolder { get; set; }
    public Func<string?>? PickFile { get; set; }

    public bool DefaultIsVideo
    {
        get => !DefaultIsAudio;
        set => DefaultIsAudio = !value;
    }

    public string FfmpegStatus =>
        DownloadEngine.FindFfmpeg(FfmpegPath) is { } p ? $"{Loc.T("S.FfmpegFound")}: {p}" : Loc.T("S.FfmpegMissing");
    public bool FfmpegOk => DownloadEngine.FindFfmpeg(FfmpegPath) != null;

    public SettingsViewModel()
    {
        _loading = true;
        _selectedLanguage = Languages.First(l => l.Code == S.Language);
        _downloadFolder = S.DownloadFolder;
        _audioDownloadFolder = S.AudioDownloadFolder;
        _defaultIsAudio = S.DefaultIsAudio;
        _defaultVideo = VideoChoices.FirstOrDefault(c => c.Value == S.DefaultVideoHeight) ?? VideoChoices[3];
        _defaultAudio = AudioChoices.FirstOrDefault(c => c.Value == S.DefaultAudioKbps) ?? AudioChoices[2];
        _createSubfolder = S.CreatePlaylistSubfolder;
        _numberItems = S.NumberPlaylistItems;
        _includeChaptersByDefault = S.IncludeChaptersByDefault;
        _maxConcurrent = S.MaxConcurrent;
        _autoStart = S.AutoStartQueue;
        _ffmpegPath = S.FfmpegPath;
        _loading = false;
        Loc.Changed += () => { OnPropertyChanged(nameof(FfmpegStatus)); };
    }

    void Commit(Action set) { if (_loading) return; set(); SettingsStore.Save(); }

    partial void OnSelectedLanguageChanged(LanguageOption value) => Commit(() => { S.Language = value.Code; Loc.Apply(value.Code); });
    partial void OnDownloadFolderChanged(string value) => Commit(() => S.DownloadFolder = value);
    partial void OnAudioDownloadFolderChanged(string value) => Commit(() => S.AudioDownloadFolder = value);
    partial void OnDefaultIsAudioChanged(bool value) { OnPropertyChanged(nameof(DefaultIsVideo)); Commit(() => S.DefaultIsAudio = value); }
    partial void OnDefaultVideoChanged(QualityChoice value) => Commit(() => S.DefaultVideoHeight = value.Value);
    partial void OnDefaultAudioChanged(QualityChoice value) => Commit(() => S.DefaultAudioKbps = value.Value);
    partial void OnCreateSubfolderChanged(bool value) => Commit(() => S.CreatePlaylistSubfolder = value);
    partial void OnNumberItemsChanged(bool value) => Commit(() => S.NumberPlaylistItems = value);
    partial void OnIncludeChaptersByDefaultChanged(bool value) => Commit(() => S.IncludeChaptersByDefault = value);
    partial void OnMaxConcurrentChanged(int value) => Commit(() => S.MaxConcurrent = value);
    partial void OnAutoStartChanged(bool value) => Commit(() => S.AutoStartQueue = value);
    partial void OnFfmpegPathChanged(string value)
    {
        Commit(() => S.FfmpegPath = value);
        OnPropertyChanged(nameof(FfmpegStatus)); OnPropertyChanged(nameof(FfmpegOk));
    }

    [RelayCommand] void BrowseFolder() { var p = PickFolder?.Invoke(); if (!string.IsNullOrEmpty(p)) DownloadFolder = p; }
    [RelayCommand] void BrowseAudioFolder() { var p = PickFolder?.Invoke(); if (!string.IsNullOrEmpty(p)) AudioDownloadFolder = p; }
    [RelayCommand] void BrowseFfmpeg() { var p = PickFile?.Invoke(); if (!string.IsNullOrEmpty(p)) FfmpegPath = p; }
}
