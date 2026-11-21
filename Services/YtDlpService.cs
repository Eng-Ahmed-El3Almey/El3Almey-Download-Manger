using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace El3Almey.Services;

public partial class MediaTrackOption : ObservableObject
{
    public string Id { get; init; } = "";
    public string Language { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsDefault { get; init; }
    [ObservableProperty] private bool _isSelected;
    public override string ToString() => Label;
}

public partial class SubtitleFormatOption : ObservableObject
{
    public string Format { get; init; } = "SRT";
    [ObservableProperty] private bool _isSelected;
    public string Label => Format;
}

public sealed class MediaTrackInfo
{
    public List<MediaTrackOption> AudioTracks { get; init; } = new();
    public List<MediaTrackOption> SubtitleTracks { get; init; } = new();
    public bool HasChapters { get; init; }
}

public static class YtDlpService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    static readonly SemaphoreSlim Gate = new(1, 1);
    static string ToolDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "El3AlmeyDownloadManager", "Tools");
    static string ExePath => Path.Combine(ToolDir, "yt-dlp.exe");
    static string? FindJavaScriptRuntime()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", "deno.exe"),
            Path.Combine(AppContext.BaseDirectory, "deno.exe"),
            Path.Combine(ToolDir, "deno.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "El3AlmeyDownloadManager", "Tools", "deno.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    static void AddJavaScriptRuntimeArguments(List<string> args)
    {
        var deno = FindJavaScriptRuntime();
        if (!string.IsNullOrWhiteSpace(deno))
        {
            args.Add("--js-runtimes");
            args.Add("deno:" + deno);
        }
    }

    public static async Task<string> EnsureAsync(CancellationToken ct)
    {
        if (File.Exists(ExePath)) return ExePath;
        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(ExePath)) return ExePath;
            Directory.CreateDirectory(ToolDir);
            using var response = await Http.GetAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var tmp = ExePath + ".download";
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(tmp)) await input.CopyToAsync(output, ct);
            File.Move(tmp, ExePath, true);
            return ExePath;
        }
        finally { Gate.Release(); }
    }

    public sealed class VideoMetadata
    {
        public string Title { get; init; } = "";
        public string Channel { get; init; } = "";
        public TimeSpan? Duration { get; init; }
        public long Views { get; init; }
        public DateTimeOffset? Published { get; init; }
        public string Thumbnail { get; init; } = "";
    }

    public sealed class PlaylistEntryMetadata
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Channel { get; init; } = "";
        public TimeSpan? Duration { get; init; }
        public string Thumbnail { get; init; } = "";
    }

    public sealed class PlaylistMetadata
    {
        public string Title { get; init; } = "";
        public string Channel { get; init; } = "";
        public string Thumbnail { get; init; } = "";
        public List<PlaylistEntryMetadata> Entries { get; init; } = new();
    }

    public sealed class VideoFormatEstimate
    {
        public int Height { get; init; }
        public long EstimatedBytes { get; init; }
    }

    public static async Task<VideoMetadata> GetVideoMetadataAsync(string url, CancellationToken ct)
    {
        var exe = await EnsureAsync(ct);
        var output = await RunCaptureAsync(exe, new[] { "--dump-single-json", "--no-warnings", "--no-playlist", "--skip-download", url }, ct);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        long views = root.TryGetProperty("view_count", out var viewsEl) && viewsEl.ValueKind == JsonValueKind.Number ? viewsEl.GetInt64() : 0;
        TimeSpan? duration = root.TryGetProperty("duration", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(Math.Max(0, durationEl.GetDouble())) : null;
        DateTimeOffset? published = null;
        string uploadDate = Str(root, "release_date");
        if (uploadDate.Length == 0) uploadDate = Str(root, "upload_date");
        if (uploadDate.Length == 8 && DateTime.TryParseExact(uploadDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            published = new DateTimeOffset(date, TimeSpan.Zero);
        return new VideoMetadata
        {
            Title = Str(root, "title"),
            Channel = FirstNonEmpty(Str(root, "channel"), Str(root, "uploader"), Str(root, "channel_id")),
            Duration = duration,
            Views = views,
            Published = published,
            Thumbnail = Str(root, "thumbnail")
        };
    }

    public static async Task<PlaylistMetadata> GetPlaylistMetadataAsync(string url, int maxItems, CancellationToken ct)
    {
        var exe = await EnsureAsync(ct);
        var output = await RunCaptureAsync(exe, new[] { "--flat-playlist", "--dump-single-json", "--no-warnings", "--playlist-end", Math.Max(1, maxItems).ToString(CultureInfo.InvariantCulture), "--skip-download", url }, ct);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var entries = new List<PlaylistEntryMetadata>();
        if (root.TryGetProperty("entries", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string id = Str(item, "id");
                if (id.Length == 0) continue;
                double seconds = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : -1;
                entries.Add(new PlaylistEntryMetadata
                {
                    Id = id,
                    Title = Str(item, "title"),
                    Channel = FirstNonEmpty(Str(item, "channel"), Str(item, "uploader")),
                    Duration = seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null,
                    Thumbnail = Str(item, "thumbnail")
                });
                if (entries.Count >= maxItems) break;
            }
        }
        return new PlaylistMetadata
        {
            Title = FirstNonEmpty(Str(root, "title"), Str(root, "playlist_title")),
            Channel = FirstNonEmpty(Str(root, "channel"), Str(root, "uploader")),
            Thumbnail = Str(root, "thumbnail"),
            Entries = entries
        };
    }

    static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    public static async Task<List<VideoFormatEstimate>> GetVideoFormatEstimatesAsync(string url, CancellationToken ct)
    {
        var exe = await EnsureAsync(ct);
        var output = await RunCaptureAsync(exe, new[] { "--dump-single-json", "--no-warnings", "--no-playlist", "--skip-download", url }, ct);
        using var doc = JsonDocument.Parse(output);
        return ParseVideoFormatEstimates(doc.RootElement);
    }

    public static async Task<Dictionary<string, List<VideoFormatEstimate>>> GetPlaylistFormatEstimatesAsync(
        string playlistUrl, int maxItems, CancellationToken ct)
    {
        var exe = await EnsureAsync(ct);
        // --dump-json emits one complete metadata JSON object per playlist video.
        // A single yt-dlp process replaces one process per playlist entry.
        var output = await RunCaptureAsync(exe, new[]
        {
            "--dump-json", "--no-warnings", "--skip-download",
            "--playlist-end", Math.Max(1, maxItems).ToString(CultureInfo.InvariantCulture),
            playlistUrl
        }, ct);

        var result = new Dictionary<string, List<VideoFormatEstimate>>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(output);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                string id = Str(doc.RootElement, "id");
                if (id.Length == 0) continue;
                result[id] = ParseVideoFormatEstimates(doc.RootElement);
            }
            catch (JsonException) { /* Ignore non-JSON diagnostic lines. */ }
        }
        return result;
    }

    static List<VideoFormatEstimate> ParseVideoFormatEstimates(JsonElement root)
    {
        double duration = Number(root, "duration");
        var formats = root.TryGetProperty("formats", out var formatsElement) && formatsElement.ValueKind == JsonValueKind.Array
            ? formatsElement.EnumerateArray().ToList() : new List<JsonElement>();

        var videoOnly = formats.Where(f =>
            Str(f, "vcodec") is not ("" or "none") && Str(f, "acodec") == "none"
            && Number(f, "height") > 0).ToList();
        var progressive = formats.Where(f =>
            Str(f, "vcodec") is not ("" or "none") && Str(f, "acodec") is not ("" or "none")
            && Number(f, "height") > 0).ToList();
        var audioOnly = formats.Where(f =>
            Str(f, "vcodec") == "none" && Str(f, "acodec") is not ("" or "none")).ToList();

        var availableHeights = videoOnly.Concat(progressive)
            .Select(f => (int)Number(f, "height"))
            .Where(h => h > 0).Distinct().OrderBy(h => h).ToList();
        var estimates = new List<VideoFormatEstimate>();

        foreach (int targetHeight in availableHeights)
        {
            var selectedVideo = videoOnly.LastOrDefault(f => Number(f, "height") <= targetHeight);
            bool includesAudio = false;
            if (selectedVideo.ValueKind == JsonValueKind.Undefined)
            {
                selectedVideo = progressive.LastOrDefault(f => Number(f, "height") <= targetHeight);
                includesAudio = selectedVideo.ValueKind != JsonValueKind.Undefined;
            }
            if (selectedVideo.ValueKind == JsonValueKind.Undefined)
            {
                selectedVideo = formats.LastOrDefault(f =>
                    Str(f, "vcodec") is not ("" or "none") && Str(f, "acodec") is not ("" or "none") && Number(f, "height") > 0);
                includesAudio = selectedVideo.ValueKind != JsonValueKind.Undefined;
            }
            if (selectedVideo.ValueKind == JsonValueKind.Undefined) continue;

            long totalBytes = EstimateFormatBytes(selectedVideo, duration);
            if (!includesAudio)
            {
                var selectedAudio = audioOnly.LastOrDefault();
                if (selectedAudio.ValueKind != JsonValueKind.Undefined)
                    totalBytes += EstimateFormatBytes(selectedAudio, duration);
            }
            estimates.Add(new VideoFormatEstimate { Height = targetHeight, EstimatedBytes = totalBytes });
        }
        return estimates;
    }

    static long EstimateFormatBytes(JsonElement format, double duration)
    {
        long bytes = FileSize(format);
        if (bytes > 0) return bytes;
        double bitrate = Number(format, "tbr") * 1000d;
        return duration > 0 && bitrate > 0 ? (long)(bitrate / 8d * duration) : 0;
    }

    static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    static long FileSize(JsonElement format)
    {
        double size = Number(format, "filesize");
        if (size <= 0) size = Number(format, "filesize_approx");
        return size > 0 && size < long.MaxValue ? (long)size : 0;
    }

    public static async Task<MediaTrackInfo> GetTracksAsync(string url, CancellationToken ct)
    {
        var exe = await EnsureAsync(ct);
        var output = await RunCaptureAsync(exe, new[] { "--dump-single-json", "--no-warnings", "--no-playlist", url }, ct);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var audio = new Dictionary<string, MediaTrackOption>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in formats.EnumerateArray())
            {
                string acodec = Str(f, "acodec"), vcodec = Str(f, "vcodec");
                if (acodec == "none" || string.IsNullOrWhiteSpace(acodec) || vcodec != "none") continue;
                string lang = Str(f, "language");
                if (string.IsNullOrWhiteSpace(lang)) lang = "und";
                string id = Str(f, "format_id");
                if (id.Length == 0 || lang == "und") continue;
                bool def = f.TryGetProperty("language_preference", out var lp) && lp.ValueKind == JsonValueKind.Number && lp.GetInt32() >= 10;
                if (!audio.ContainsKey(lang))
                {
                    var languageName = GetLanguageName(lang);
                    audio[lang] = new MediaTrackOption { Id = lang, Language = lang, IsDefault = def, Label = languageName + (def ? " — Default" : "") };
                }
                else if (def && !audio[lang].IsDefault)
                {
                    audio[lang] = new MediaTrackOption { Id = lang, Language = lang, IsDefault = true, Label = GetLanguageName(lang) + " — Default" };
                }
            }
        }
        var subs = new Dictionary<string, MediaTrackOption>(StringComparer.OrdinalIgnoreCase);
        AddSubtitleMap(root, "subtitles", subs, false);
        AddSubtitleMap(root, "automatic_captions", subs, true);
        var audioList = audio.Values.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Language).ToList();
        audioList.Insert(0, new MediaTrackOption { Id = "default", Language = "default", IsDefault = true, Label = "Default — YouTube default audio" });
        var subList = new List<MediaTrackOption> { new() { Id = "none", Language = "none", Label = "No Subtitles" } };
        subList.AddRange(subs.Values.OrderBy(x => x.Language));
        bool hasChapters = root.TryGetProperty("chapters", out var chapters)
            && chapters.ValueKind == JsonValueKind.Array && chapters.GetArrayLength() > 0;
        return new MediaTrackInfo { AudioTracks = audioList, SubtitleTracks = subList, HasChapters = hasChapters };
    }

    static void AddSubtitleMap(JsonElement root, string key, Dictionary<string, MediaTrackOption> dest, bool automatic)
    {
        if (!root.TryGetProperty(key, out var map) || map.ValueKind != JsonValueKind.Object) return;
        // Keep the selector useful: common languages only, and one entry per base language
        // in each category. YouTube often exposes region/script variants that look duplicated.
        var popular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ar", "en", "es", "fr", "de", "pt", "ru", "ja", "ko", "hi", "tr", "it", "id", "zh", "bn", "ur", "fa", "nl", "pl", "uk", "vi", "th" };
        foreach (var item in map.EnumerateObject())
        {
            var baseCode = item.Name.Replace('_', '-').Split('-')[0];
            if (!popular.Contains(baseCode)) continue;
            string prefix = automatic ? "auto:" : "original:";
            // Keep the exact YouTube language key for yt-dlp (for example en-orig), while
            // de-duplicating variants by their base language in the UI.
            if (dest.Values.Any(t => t.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && t.Language.Replace('_', '-').Split('-')[0].Equals(baseCode, StringComparison.OrdinalIgnoreCase))) continue;
            string id = prefix + item.Name;
            string label = GetLanguageName(baseCode) + (automatic ? " — Generated" : " — Original");
            dest[id] = new MediaTrackOption { Id = id, Language = item.Name, Label = label };
        }
    }

    static string GetLanguageName(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("und", StringComparison.OrdinalIgnoreCase)) return "Unknown language";
        try
        {
            var normalized = code.Replace('_', '-');
            var culture = CultureInfo.GetCultureInfo(normalized);
            var baseCulture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .FirstOrDefault(c => c.TwoLetterISOLanguageName.Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
            return baseCulture?.EnglishName ?? culture.EnglishName;
        }
        catch
        {
            var baseCode = code.Split('-', '_')[0];
            try { return CultureInfo.GetCultureInfo(baseCode).EnglishName; }
            catch { return code.ToUpperInvariant(); }
        }
    }

    static string Str(JsonElement el, string name) => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    public static async Task DownloadAsync(ViewModels.DownloadItem item, string ffmpegPath, CancellationToken ct, Action<double>? progress = null)
    {
        var exe = await EnsureAsync(ct);
        Directory.CreateDirectory(item.OutputFolder);
        if (item.IsSubtitleOnly)
        {
            await DownloadSubtitlesOnlyAsync(item, exe, ct);
            item.Progress = 100;
            return;
        }
        var workspace = TempFileService.CreateWorkspace(item.OutputFolder, item.Id);
        bool completed = false;
        try
        {
        var args = new List<string>();
        AddJavaScriptRuntimeArguments(args);
        args.AddRange(new[]
        {
            "--no-warnings", "--newline", "--no-playlist", "--windows-filenames", "--progress",
            // Emit progress more frequently so the UI does not appear frozen and then jump.
            "--progress-delta", "0.5",
            // Resume partial files when a connection drops; retain fragment data for retry.
            "--continue", "--no-overwrites", "--keep-fragments",
            "--progress-template", "download:__EL3ALMEY_PROGRESS__%(progress._percent_str)s",
            "--ffmpeg-location", ffmpegPath,
            // Be more tolerant of temporary network/CDN failures instead of failing an item immediately.
            "--retries", "10",
            "--fragment-retries", "10",
            "--extractor-retries", "5",
            "--file-access-retries", "5",
            "--socket-timeout", "30",
            "--retry-sleep", "http:exp=1:20",
            "--retry-sleep", "fragment:exp=1:10",
            "--concurrent-fragments", "4",
            "--output", Path.Combine(workspace, Fmt.Sanitize(item.FileBaseName) + ".%(ext)s")
        });
        string audioLang = string.IsNullOrWhiteSpace(item.AudioTrackLanguage) || item.AudioTrackLanguage == "default" ? "" : item.AudioTrackLanguage;
        if (item.IsAudio)
        {
            args.Add("-f"); args.Add(string.IsNullOrEmpty(audioLang) ? "bestaudio" : $"bestaudio[language^={audioLang.Split('-', '_')[0]}]");
            args.Add("--extract-audio"); args.Add("--audio-format"); args.Add(item.Format.Equals("M4A", StringComparison.OrdinalIgnoreCase) ? "m4a" : "mp3");
            args.Add("--audio-quality"); args.Add(Math.Clamp(item.Kbps, 64, 320).ToString() + "K");
            args.Add("--embed-thumbnail");
        }
        else
        {
            args.Add("-f"); args.Add(string.IsNullOrEmpty(audioLang) ? $"bestvideo[height<={item.Height}]+bestaudio/best[height<={item.Height}]/best" : $"bestvideo[height<={item.Height}]+bestaudio[language^={audioLang.Split('-', '_')[0]}]");
            args.Add("--merge-output-format"); args.Add(item.Format.Equals("MKV", StringComparison.OrdinalIgnoreCase) ? "mkv" : "mp4");
            if (item.IncludeChapters) args.Add("--embed-chapters");
        }
        args.Add(item.Url);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp could not start.");
        var stderrBuilder = new System.Text.StringBuilder();
        var stdoutBuilder = new System.Text.StringBuilder();
        // yt-dlp normally writes progress to stderr, but versions/builds may route it
        // differently. Parse both redirected streams so the UI never jumps straight to 100%.
        var errTask = ReadProgressAsync(process.StandardError, progress, stderrBuilder, ct);
        var outTask = ReadProgressAsync(process.StandardOutput, progress, stdoutBuilder, ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        await Task.WhenAll(errTask, outTask);
        var stdout = stdoutBuilder.ToString();
        if (process.ExitCode != 0)
        {
            var details = stderrBuilder.ToString().Trim();
            if (details.Length == 0) details = stdout.Trim();
            throw new InvalidOperationException("yt-dlp download failed: " + details);
        }
        var expected = Path.Combine(workspace, Fmt.Sanitize(item.FileBaseName));
        var file = Directory.GetFiles(workspace, Fmt.Sanitize(item.FileBaseName) + ".*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (file == null) throw new FileNotFoundException("yt-dlp finished but the output media file could not be located. " + expected);
        item.FilePath = TempFileService.MoveToDestination(file, Path.Combine(item.OutputFolder, Path.GetFileName(file)));
        item.Progress = 96;

        // Subtitle download is deliberately separate: a missing/blocked caption must not fail the video.
        await DownloadSubtitleBestEffortAsync(item, exe, ct);
        item.Progress = 100;
        completed = true;
        }
        finally
        {
            // Preserve partial files after cancellation or failure so yt-dlp can resume them on retry.
            if (completed) TempFileService.TryDeleteWorkspace(workspace);
        }
    }

    static async Task DownloadSubtitleBestEffortAsync(ViewModels.DownloadItem item, string exe, CancellationToken ct)
    {
        var selections = (item.SubtitleTrackLanguage ?? "none")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !value.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var formats = (item.SubtitleFormats ?? "VTT").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => new[] { "srt", "vtt", "ass", "lrc" }.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (formats.Length == 0) formats = new[] { "vtt" };
        foreach (var selection in selections)
        foreach (var format in formats)
        {
            try
            {
                await DownloadSingleSubtitleBestEffortAsync(item, exe, ct, selection, format);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { LogSubtitleResult(item, selection + " [" + format + "]", -1, 0, "Subtitle exception: " + ex); }
        }
    }

    static async Task DownloadSubtitlesOnlyAsync(ViewModels.DownloadItem item, string exe, CancellationToken ct)
    {
        var selections = (item.SubtitleTrackLanguage ?? "none").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.Equals("none", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var formats = (item.SubtitleFormats ?? "SRT").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => new[] { "srt", "vtt", "ass", "lrc" }.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selections.Length == 0) throw new InvalidOperationException("Select at least one subtitle language.");
        if (formats.Length == 0) throw new InvalidOperationException("Select at least one subtitle format.");
        int created = 0;
        foreach (var selection in selections)
        foreach (var format in formats)
        {
            ct.ThrowIfCancellationRequested();
            created += await DownloadSingleSubtitleBestEffortAsync(item, exe, ct, selection, format);
        }
        if (created == 0) throw new InvalidOperationException("YouTube did not provide any of the selected subtitle tracks or formats for this video.");
        item.FilePath = item.OutputFolder;
    }

    static async Task<int> DownloadSingleSubtitleBestEffortAsync(ViewModels.DownloadItem item, string exe, CancellationToken ct, string selected, string outputFormat)
    {
        bool automatic = selected.StartsWith("auto:", StringComparison.OrdinalIgnoreCase);
        bool original = selected.StartsWith("original:", StringComparison.OrdinalIgnoreCase);
        string language = automatic || original
            ? selected[(selected.IndexOf(':') + 1)..]
            : selected;

        if (string.IsNullOrWhiteSpace(language)) return 0;

        Directory.CreateDirectory(item.OutputFolder);
        string baseName = Fmt.Sanitize(item.FileBaseName);
        string workspace = TempFileService.CreateWorkspace(item.OutputFolder, item.Id);
        // Keep original and generated captions separate, even when both are VTT in the same language.
        string captionKind = automatic ? "generated" : original ? "original" : "subtitle";
        string outputStem = Fmt.Sanitize($"{baseName}.{captionKind}.{language}");
        string outputTemplate = Path.Combine(workspace, outputStem + ".%(ext)s");
        int exitCode = -1;
        string stdout = "";
        string stderr = "";
        int movedCount = 0;

        try
        {
            var runArgs = new List<string>();
            AddJavaScriptRuntimeArguments(runArgs);
            runArgs.AddRange(new[]
            {
                "--no-playlist",
                "--skip-download",
                automatic ? "--write-auto-subs" : "--write-subs",
                "--sub-langs", language,
                "--sub-format", "best",
                "--convert-subs", outputFormat,
                "--retries", "5",
                "--extractor-retries", "5",
                "--retry-sleep", "http:exp=5:60",
                "--sleep-requests", "1",
                "--output", outputTemplate,
                item.Url
            });

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in runArgs) psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp could not start the subtitle process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
                stdout = await stdoutTask;
                stderr = await stderrTask;
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            var found = Directory.Exists(workspace)
                ? Directory.EnumerateFiles(workspace, outputStem + ".*", SearchOption.TopDirectoryOnly)
                    .Where(IsSubtitleFile).ToList()
                : new List<string>();

            foreach (var source in found)
            {
                var destination = Path.Combine(item.OutputFolder, Path.GetFileName(source));
                File.Move(source, destination, true);
                movedCount++;
            }

            LogSubtitleResult(item, selected + " [" + outputFormat + "]", exitCode, movedCount,
                $"yt-dlp subtitle output. stderr: {stderr}{Environment.NewLine}stdout: {stdout}");
            return movedCount;
        }
        finally
        {
            TempFileService.TryDeleteWorkspace(workspace);
        }
    }

    static void LogSubtitleResult(ViewModels.DownloadItem item, string selected, int exitCode, int filesCreated, string details)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "El3AlmeyDownloadManager", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "subtitle-warnings.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {item.Title} [{selected}] ExitCode={exitCode}; FilesCreated={filesCreated}{Environment.NewLine}{details}{Environment.NewLine}---{Environment.NewLine}");
        }
        catch { }
    }

    static bool IsSubtitleFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ass", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lrc", StringComparison.OrdinalIgnoreCase);
    }

    static readonly Regex ProgressPercentRegex = new(@"(?<percent>\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static async Task ReadProgressAsync(StreamReader reader, Action<double>? progress, System.Text.StringBuilder captured, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;
            captured.AppendLine(line);

            // Prefer our explicit progress-template marker. Fall back to standard yt-dlp
            // output formats for compatibility with older yt-dlp versions.
            var candidate = line;
            var marker = line.IndexOf("__EL3ALMEY_PROGRESS__", StringComparison.Ordinal);
            if (marker >= 0) candidate = line[(marker + "__EL3ALMEY_PROGRESS__".Length)..];
            else if (!line.Contains("[download]", StringComparison.OrdinalIgnoreCase)) continue;

            var match = ProgressPercentRegex.Match(candidate);
            if (match.Success && double.TryParse(match.Groups["percent"].Value,
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                progress?.Invoke(Math.Clamp(pct, 0, 95));
        }
    }

    static async Task<string> RunCaptureAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var allArgs = new List<string>();
        AddJavaScriptRuntimeArguments(allArgs);
        allArgs.AddRange(args);
        foreach (var arg in allArgs) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp could not start.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (p.ExitCode != 0) throw new InvalidOperationException("Could not read YouTube tracks: " + stderr.Trim());
        return stdout;
    }
}
