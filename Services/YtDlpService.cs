using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace El3Almey.Services;

public sealed class MediaTrackOption
{
    public string Id { get; init; } = "";
    public string Language { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsDefault { get; init; }
    public override string ToString() => Label;
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
        var args = new List<string>
        {
            "--no-warnings", "--newline", "--no-playlist", "--windows-filenames", "--progress",
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
            "--output", Path.Combine(item.OutputFolder, Fmt.Sanitize(item.FileBaseName) + ".%(ext)s")
        };
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
        var expected = Path.Combine(item.OutputFolder, Fmt.Sanitize(item.FileBaseName));
        var file = Directory.GetFiles(item.OutputFolder, Fmt.Sanitize(item.FileBaseName) + ".*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (file == null) throw new FileNotFoundException("yt-dlp finished but the output media file could not be located. " + expected);
        item.FilePath = file;
        item.Progress = 96;

        // Subtitle download is deliberately separate: a missing/blocked caption must not fail the video.
        await DownloadSubtitleBestEffortAsync(item, exe, ct);
        item.Progress = 100;
    }

    static async Task DownloadSubtitleBestEffortAsync(ViewModels.DownloadItem item, string exe, CancellationToken ct)
    {
        string selected = item.SubtitleTrackLanguage ?? "none";
        if (string.IsNullOrWhiteSpace(selected) || selected.Equals("none", StringComparison.OrdinalIgnoreCase)) return;

        bool automatic = selected.StartsWith("auto:", StringComparison.OrdinalIgnoreCase);
        string language = selected.StartsWith("auto:", StringComparison.OrdinalIgnoreCase) || selected.StartsWith("original:", StringComparison.OrdinalIgnoreCase)
            ? selected[(selected.IndexOf(':') + 1)..]
            : selected; // backward compatibility with items saved by earlier versions
        if (string.IsNullOrWhiteSpace(language)) return;

        var args = new List<string> { "--no-warnings", "--no-playlist", "--skip-download", "--sub-langs", language, "--sub-format", "vtt/best", "--output", Path.Combine(item.OutputFolder, Fmt.Sanitize(item.FileBaseName) + ".%(ext)s") };
        args.Add(automatic ? "--write-auto-subs" : "--write-subs");
        args.Add(item.Url);

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi);
        if (process == null) return;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                try
                {
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "El3AlmeyDownloadManager", "logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "subtitle-warnings.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {item.Title} [{language}; {(automatic ? "generated" : "original")}] ExitCode={process.ExitCode}{Environment.NewLine}{stderr}{Environment.NewLine}{stdout}{Environment.NewLine}---{Environment.NewLine}");
                }
                catch { }
            }
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
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
                progress?.Invoke(Math.Clamp(pct, 0, 100));
        }
    }

    static async Task<string> RunCaptureAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
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
