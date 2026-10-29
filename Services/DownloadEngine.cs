using System.Net.Http;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Windows;
using System.Windows.Threading;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using El3Almey.ViewModels;

namespace El3Almey.Services;

public static class DownloadEngine
{
    static readonly YoutubeClient Yt = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    static readonly SemaphoreSlim FfmpegGate = new(1, 1);
    static readonly ConcurrentDictionary<Guid, int> LastProgressBucket = new();
    const string FfmpegArchiveUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    // FFmpeg is managed by the application; users do not need to install it manually.
    static async Task<string> EnsureFfmpegAsync(string custom, CancellationToken ct)
    {
        var found = FindFfmpeg(custom);
        if (found != null) return found;

        await FfmpegGate.WaitAsync(ct);
        try
        {
            found = FindFfmpeg(custom);
            if (found != null) return found;

            var toolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "El3AlmeyDownloadManager", "Tools", "ffmpeg");
            var exe = Path.Combine(toolDir, "bin", "ffmpeg.exe");
            if (File.Exists(exe)) return exe;
            Directory.CreateDirectory(toolDir);
            var archive = Path.Combine(toolDir, "ffmpeg-download.zip");
            try
            {
                using (var response = await Http.GetAsync(FfmpegArchiveUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var target = File.Create(archive);
                    await source.CopyToAsync(target, ct);
                }

                await Task.Run(() => ExtractFfmpegArchive(archive, toolDir, exe, ct), ct);
                return exe;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException("The app could not prepare its built-in FFmpeg tools. Check your internet connection and retry. Details: " + ex.Message, ex);
            }
            finally { try { if (File.Exists(archive)) File.Delete(archive); } catch { } }
        }
        finally { FfmpegGate.Release(); }
    }


    static void ExtractFfmpegArchive(string archive, string toolDir, string exe, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var normalized = entry.FullName.Replace('\\', '/');
            var marker = normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
            string relative;
            if (marker >= 0) relative = normalized[(marker + 5)..];
            else if (normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)) relative = normalized[4..];
            else continue;
            if (string.IsNullOrWhiteSpace(relative)) continue;
            // The archive's bin folder contains many non-runtime files. Keep only executables
            // and runtime DLLs required by FFmpeg to reduce first-run extraction work.
            var extension = Path.GetExtension(relative);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var destination = Path.GetFullPath(Path.Combine(toolDir, "bin", relative));
            var safeRoot = Path.GetFullPath(Path.Combine(toolDir, "bin")) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
        if (!File.Exists(exe)) throw new InvalidOperationException("The FFmpeg archive did not contain ffmpeg.exe.");
    }

    public static string? FindFfmpeg(string custom)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (File.Exists(custom)) return custom;
            var p = Path.Combine(custom, "ffmpeg.exe");
            if (Directory.Exists(custom) && File.Exists(p)) return p;
        }
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    // Start preparing both command-line tools as soon as the application opens, so the first
    // user download does not have to wait for tool downloads/extraction whenever possible.
    public static async Task PrepareToolsAsync(string customFfmpeg, CancellationToken ct)
    {
        var ffmpegTask = EnsureFfmpegAsync(customFfmpeg, ct);
        var ytDlpTask = YtDlpService.EnsureAsync(ct);
        await Task.WhenAll(ffmpegTask, ytDlpTask);
    }

    public static async Task RunAsync(DownloadItem it, AppSettings s, CancellationToken ct)
    {
        Directory.CreateDirectory(it.OutputFolder);
        // Run the two first-run setup tasks concurrently; the app startup warm-up shares the same gates.
        var ffmpegTask = EnsureFfmpegAsync(s.FfmpegPath, ct);
        var ytDlpTask = YtDlpService.EnsureAsync(ct);
        await Task.WhenAll(ffmpegTask, ytDlpTask);
        await YtDlpService.DownloadAsync(it, ffmpegTask.Result, ct, p => ReportProgress(it, p));
    }

    // Download process output/progress can arrive on worker threads. Never update an
    // ObservableObject bound to WPF directly from those threads.
    static void ReportProgress(DownloadItem item, double value)
    {
        // yt-dlp may emit many progress lines per second. Limit bound-property updates to
        // at most one update per whole percentage point to avoid flooding the WPF Dispatcher.
        int bucket = (int)Math.Clamp(Math.Floor(value), 0, 100);
        int previous = LastProgressBucket.GetOrAdd(item.Id, -1);
        if (bucket == previous) return;
        LastProgressBucket[item.Id] = bucket;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            // Progress is monotonic for a single download item. yt-dlp may report a fresh
            // per-process percentage after Resume; never let that reset the saved UI progress.
            if (value > item.Progress) item.Progress = value;
            return;
        }
        if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (value > item.Progress) item.Progress = value;
            }));
    }

    public static void ClearProgressState(Guid itemId) => LastProgressBucket.TryRemove(itemId, out _);

    // ---------------------------------------------------------------- audio
    static async Task DownloadAudio(DownloadItem it, StreamManifest m, string? ffmpeg, string baseName, CancellationToken ct)
    {
        var wantM4a = string.Equals(it.Format, "M4A", StringComparison.OrdinalIgnoreCase);
        var a = StreamPicker.Audio(m, wantM4a) ?? throw new Exception(Loc.T("S.Err.NoStream"));
        it.TotalBytes = a.Size.Bytes;
        string ext = a.Container.Name == "mp4" ? "m4a" : a.Container.Name;
        string tmp = Path.Combine(it.OutputFolder, baseName + ".part." + ext);
        double scale = 92;
        try
        {
            await Dl(a, tmp, p => ReportProgress(it, p * scale), ct);

            if (ffmpeg != null)
            {
                string audioFormat = string.Equals(it.Format, "M4A", StringComparison.OrdinalIgnoreCase) ? "m4a" : "mp3";
                string final = Unique(Path.Combine(it.OutputFolder, baseName + "." + audioFormat));
                string? cover = null;
                try
                {
                    byte[] bytes;
                    try { bytes = await Http.GetByteArrayAsync(YouTubeService.Thumb(it.VideoId), ct); }
                    catch { bytes = await Http.GetByteArrayAsync(YouTubeService.Thumb(it.VideoId, "default"), ct); }
                    cover = Path.Combine(it.OutputFolder, baseName + ".cover.jpg");
                    await File.WriteAllBytesAsync(cover, bytes, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw new Exception("Could not retrieve the YouTube thumbnail for audio embedding: " + ex.Message, ex); }
                string codec = audioFormat == "m4a" ? "aac" : "libmp3lame";
                string args = $"-y -i \"{tmp}\" -i \"{cover}\" -map 0:a -map 1:v -c:a {codec} -b:a {it.Kbps}k -c:v mjpeg -disposition:v:0 attached_pic -id3v2_version 3 -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" \"{final}\"";
                try { await RunFfmpeg(ffmpeg, args, ct); }
                finally { if (cover != null) TryDelete(cover); }
                it.FilePath = final;
            }
            else
            {
                if (string.Equals(it.Format, "MP3", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("FFmpeg is required to convert audio to MP3. Configure ffmpeg.exe in Settings.");
                if (wantM4a && ext != "m4a" && ext != "mp4")
                    throw new Exception("FFmpeg is required to convert this audio stream to M4A. Configure ffmpeg.exe in Settings.");
                // Without FFmpeg, keep the original compatible audio container.
                string final = Unique(Path.Combine(it.OutputFolder, baseName + "." + (ext == "mp4" ? "m4a" : ext)));
                File.Move(tmp, final);
                it.FilePath = final;
            }
        }
        finally { TryDelete(tmp); }
    }

    // ---------------------------------------------------------------- video
    static async Task DownloadVideo(DownloadItem it, StreamManifest m, string? ffmpeg, string baseName, CancellationToken ct)
    {
        var v = StreamPicker.Pick(m, it.Height) ?? throw new Exception(Loc.T("S.Err.NoStream"));
        bool mp4 = v.Container.Name == "mp4";
        var a = StreamPicker.Audio(m, mp4) ?? throw new Exception(Loc.T("S.Err.NoStream"));
        long vb = v.Size.Bytes, ab = a.Size.Bytes;
        it.TotalBytes = vb + ab;

        double vf = 0, af = 0;
        void Report() => ReportProgress(it, (vf * vb + af * ab) / Math.Max(1, vb + ab) * 95);

        string vt = Path.Combine(it.OutputFolder, baseName + ".part.v." + v.Container.Name);
        string at = Path.Combine(it.OutputFolder, baseName + ".part.a." + a.Container.Name);
        try
        {
            await Task.WhenAll(
                Dl(v, vt, p => { vf = p; Report(); }, ct),
                Dl(a, at, p => { af = p; Report(); }, ct));

            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
                throw new InvalidOperationException("FFmpeg is required to merge the selected video and audio streams, but its executable was not found. Configure ffmpeg.exe in Settings or retry after the app prepares its built-in tools.");

            string requestedFormat = string.Equals(it.Format, "MKV", StringComparison.OrdinalIgnoreCase) ? "mkv" : "mp4";
            string final = Unique(Path.Combine(it.OutputFolder, baseName + "." + requestedFormat));
            string flags = requestedFormat == "mp4" ? "-movflags +faststart " : "";
            await RunFfmpeg(ffmpeg, $"-y -i \"{vt}\" -i \"{at}\" -c copy {flags}\"{final}\"", ct);
            it.FilePath = final;
        }
        finally { TryDelete(vt); TryDelete(at); }
    }

    // ---------------------------------------------------------------- helpers
    static Task Dl(IStreamInfo s, string path, Action<double> cb, CancellationToken ct) =>
        Yt.Videos.Streams.DownloadAsync(s, path, new Progress<double>(cb), ct).AsTask();

    static async Task RunFfmpeg(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var p = Process.Start(psi) ?? throw new Exception("ffmpeg failed to start");
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }
        if (p.ExitCode != 0)
        {
            var text = (await err).Trim();
            var last = text.Split('\n').LastOrDefault() ?? "";
            throw new Exception("ffmpeg: " + last.Trim());
        }
    }

    static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
