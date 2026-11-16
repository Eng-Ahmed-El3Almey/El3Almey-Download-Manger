using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace El3Almey.Services;

public sealed record StartupProgress(string Message, double Percent);

public static class ToolBootstrapService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    private static string ToolsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "El3AlmeyDownloadManager", "Tools");

    private static string BuiltInFfmpeg => Path.Combine(ToolsDirectory, "ffmpeg", "bin", "ffmpeg.exe");
    private static string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");

    public static async Task<IReadOnlyList<string>> CheckAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        string ffmpeg = ResolveConfiguredFfmpeg();
        if (!await IsRunnableAsync(ffmpeg, "-version", cancellationToken))
            missing.Add("FFmpeg");

        if (!await IsRunnableAsync(YtDlpPath, "--version", cancellationToken))
            missing.Add("yt-dlp");

        return missing;
    }

    public static async Task PrepareAsync(
        IProgress<StartupProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ToolsDirectory);

        bool customFfmpeg = !string.IsNullOrWhiteSpace(SettingsStore.Current.FfmpegPath);
        bool ffmpegReady = await IsRunnableAsync(ResolveConfiguredFfmpeg(), "-version", cancellationToken);

        if (!ffmpegReady && customFfmpeg)
        {
            SettingsStore.Current.FfmpegPath = "";
            SettingsStore.Save();
            ffmpegReady = await IsRunnableAsync(BuiltInFfmpeg, "-version", cancellationToken);
        }

        if (!ffmpegReady)
        {
            progress.Report(new StartupProgress("Downloading FFmpeg...", 0));
            await DownloadAndExtractFfmpegAsync(progress, cancellationToken);
            progress.Report(new StartupProgress("Verifying FFmpeg...", 65));
            if (!await IsRunnableAsync(BuiltInFfmpeg, "-version", cancellationToken))
                throw new InvalidOperationException("FFmpeg was downloaded, but its executable could not be started.");
        }
        else
        {
            progress.Report(new StartupProgress("FFmpeg is ready", 65));
        }

        if (!await IsRunnableAsync(YtDlpPath, "--version", cancellationToken))
        {
            progress.Report(new StartupProgress("Downloading yt-dlp...", 66));
            await DownloadYtDlpAsync(progress, cancellationToken);
        }
        else
        {
            progress.Report(new StartupProgress("yt-dlp is ready", 95));
        }

        progress.Report(new StartupProgress("Verifying required tools...", 97));
        if (!await IsRunnableAsync(ResolveConfiguredFfmpeg(), "-version", cancellationToken))
            throw new InvalidOperationException("FFmpeg failed its final verification.");
        if (!await IsRunnableAsync(YtDlpPath, "--version", cancellationToken))
            throw new InvalidOperationException("yt-dlp failed its final verification.");

        progress.Report(new StartupProgress("All required tools are ready", 100));
    }

    private static string ResolveConfiguredFfmpeg()
    {
        string custom = SettingsStore.Current.FfmpegPath ?? "";
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (Directory.Exists(custom))
            {
                string candidate = Path.Combine(custom, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(custom, "bin", "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            if (File.Exists(custom)) return custom;
        }
        return BuiltInFfmpeg;
    }

    private static async Task<bool> IsRunnableAsync(string executable, string argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return false;

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task DownloadYtDlpAsync(
        IProgress<StartupProgress> progress,
        CancellationToken cancellationToken)
    {
        string temporaryPath = YtDlpPath + ".download";
        try
        {
            using var response = await Http.GetAsync(YtDlpUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                double percent = total > 0 ? 66 + (received * 29d / total) : 66;
                progress.Report(new StartupProgress(
                    total > 0 ? $"Downloading yt-dlp... {received / 1048576d:0.0} / {total / 1048576d:0.0} MB"
                              : $"Downloading yt-dlp... {received / 1048576d:0.0} MB",
                    Math.Min(95, percent)));
            }
            await output.FlushAsync(cancellationToken);
            File.Move(temporaryPath, YtDlpPath, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }

        if (!await IsRunnableAsync(YtDlpPath, "--version", cancellationToken))
            throw new InvalidOperationException("yt-dlp was downloaded, but its executable could not be started.");
    }

    private static async Task DownloadAndExtractFfmpegAsync(
        IProgress<StartupProgress> progress,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(ToolsDirectory, "ffmpeg");
        string archive = Path.Combine(directory, "ffmpeg-download.zip");
        Directory.CreateDirectory(directory);
        try
        {
            using (var response = await Http.GetAsync(FfmpegUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    double percent = total > 0 ? received * 55d / total : 0;
                    progress.Report(new StartupProgress(
                        total > 0 ? $"Downloading FFmpeg... {received / 1048576d:0.0} / {total / 1048576d:0.0} MB"
                                  : $"Downloading FFmpeg... {received / 1048576d:0.0} MB",
                        Math.Min(55, percent)));
                }
                await output.FlushAsync(cancellationToken);
            }

            progress.Report(new StartupProgress("Extracting FFmpeg...", 58));
            await Task.Run(() => ExtractFfmpeg(archive, directory, progress, cancellationToken), cancellationToken);
            progress.Report(new StartupProgress("FFmpeg extraction completed", 64));
        }
        finally
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
        }
    }

    private static void ExtractFfmpeg(
        string archive,
        string toolDirectory,
        IProgress<StartupProgress> progress,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archive);
        int totalEntries = Math.Max(1, zip.Entries.Count);
        int processedEntries = 0;

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedEntries++;
            double extractionPercent = 58 + processedEntries * 6d / totalEntries;
            progress.Report(new StartupProgress("Extracting FFmpeg...", Math.Min(64, extractionPercent)));
            string normalized = entry.FullName.Replace('\\', '/');
            int marker = normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
            string relative;
            if (marker >= 0) relative = normalized[(marker + 5)..];
            else if (normalized.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)) relative = normalized[4..];
            else continue;

            if (string.IsNullOrWhiteSpace(relative)) continue;
            string extension = Path.GetExtension(relative);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) continue;

            string destination = Path.GetFullPath(Path.Combine(toolDirectory, "bin", relative));
            string safeRoot = Path.GetFullPath(Path.Combine(toolDirectory, "bin")) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!entry.FullName.EndsWith("/", StringComparison.Ordinal))
                entry.ExtractToFile(destination, true);
        }

        if (!File.Exists(Path.Combine(toolDirectory, "bin", "ffmpeg.exe")))
            throw new InvalidOperationException("The FFmpeg archive did not contain ffmpeg.exe.");
    }
}
