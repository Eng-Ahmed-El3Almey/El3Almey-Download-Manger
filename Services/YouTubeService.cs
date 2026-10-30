using YoutubeExplode;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace El3Almey.Services;

public class EntryInfo
{
    public int Index;
    public string Id = "";
    public string Title = "";
    public string Channel = "";
    public TimeSpan? Duration;
    public string Thumb = "";
    public List<YtDlpService.VideoFormatEstimate> VideoFormats = new();
}

public class ResolveResult
{
    public bool IsPlaylist;
    public string VideoId = "";          // the video (or the first video of the playlist)
    public string Title = "";
    public string Channel = "";
    public TimeSpan? Duration;           // video duration, or playlist total
    public long Views;
    public DateTimeOffset? Published;
    public string Thumb = "";
    public string PlaylistTitle = "";
    public string? SingleVideoId;        // set when the link had both v= and list=
    public List<EntryInfo> Entries = new();
    public StreamManifest Manifest = null!;
    public MediaTrackInfo Tracks = new();
}

public static class YouTubeService
{
    static readonly YoutubeClient Yt = new();
    const int MaxPlaylistItems = 1000;

    public static string Thumb(string id, string quality = "hqdefault") => $"https://i.ytimg.com/vi/{id}/{quality}.jpg";

    public static bool LooksLikeYouTube(string url) =>
        VideoId.TryParse(url) != null || PlaylistId.TryParse(url) != null;

    public static bool HasVideoAndPlaylist(string url) =>
        VideoId.TryParse(url) != null && PlaylistId.TryParse(url) != null;

    public static async Task<ResolveResult> ResolveAsync(string url, bool forceSingle, CancellationToken ct)
    {
        var vid = VideoId.TryParse(url);
        var pid = PlaylistId.TryParse(url);

        // Prefer yt-dlp for resolving YouTube metadata because YoutubeExplode can
        // reject otherwise valid videos/playlists when YouTube changes its responses.
        if (pid != null && !forceSingle)
        {
            try { return await ResolvePlaylistWithYtDlp(url, pid.Value, vid, ct); }
            catch (OperationCanceledException) { throw; }
            catch when (vid != null)
            {
                // Mixed video/playlist URLs (including generated mixes) may not expose
                // a readable playlist; resolve the explicitly linked video instead.
            }
        }

        if (vid == null)
            throw new InvalidOperationException("Unsupported YouTube link. Please check that the video or playlist is public and available.");

        try
        {
            return await ResolveVideoWithYtDlp(url, vid.Value, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ytDlpError)
        {
            try { return await ResolveVideoWithYoutubeExplode(vid.Value, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception fallbackError)
            {
                throw new InvalidOperationException(
                    "Could not load this YouTube video. yt-dlp: " + ytDlpError.Message +
                    " | fallback: " + fallbackError.Message, ytDlpError);
            }
        }
    }

    static async Task<ResolveResult> ResolveVideoWithYtDlp(string url, VideoId id, CancellationToken ct)
    {
        var metadata = await YtDlpService.GetVideoMetadataAsync(url, ct);
        var manifest = await Yt.Videos.Streams.GetManifestAsync(id, ct);
        var tracks = await YtDlpService.GetTracksAsync("https://www.youtube.com/watch?v=" + id.Value, ct);
        return new ResolveResult
        {
            IsPlaylist = false,
            VideoId = id.Value,
            Title = metadata.Title,
            Channel = metadata.Channel,
            Duration = metadata.Duration,
            Views = metadata.Views,
            Published = metadata.Published,
            Thumb = string.IsNullOrWhiteSpace(metadata.Thumbnail) ? Thumb(id.Value) : metadata.Thumbnail,
            Manifest = manifest,
            Tracks = tracks
        };
    }

    static async Task<ResolveResult> ResolveVideoWithYoutubeExplode(VideoId id, CancellationToken ct)
    {
        var video = await Yt.Videos.GetAsync(id, ct);
        var manifest = await Yt.Videos.Streams.GetManifestAsync(id, ct);
        var tracks = await YtDlpService.GetTracksAsync("https://www.youtube.com/watch?v=" + id.Value, ct);
        return new ResolveResult
        {
            IsPlaylist = false,
            VideoId = id.Value,
            Title = video.Title,
            Channel = video.Author.ChannelTitle,
            Duration = video.Duration,
            Views = video.Engagement.ViewCount,
            Published = video.UploadDate,
            Thumb = Thumb(id.Value),
            Manifest = manifest,
            Tracks = tracks
        };
    }

    static async Task<ResolveResult> ResolvePlaylistWithYtDlp(string url, PlaylistId pid, VideoId? currentVideo, CancellationToken ct)
    {
        var metadata = await YtDlpService.GetPlaylistMetadataAsync(url, MaxPlaylistItems, ct);
        var res = new ResolveResult
        {
            IsPlaylist = true,
            PlaylistTitle = string.IsNullOrWhiteSpace(metadata.Title) ? pid.Value : metadata.Title,
            Title = string.IsNullOrWhiteSpace(metadata.Title) ? pid.Value : metadata.Title,
            Channel = metadata.Channel,
            SingleVideoId = currentVideo?.Value
        };

        int i = 0;
        TimeSpan total = TimeSpan.Zero;
        foreach (var entry in metadata.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Id)) continue;
            i++;
            var duration = entry.Duration;
            res.Entries.Add(new EntryInfo
            {
                Index = i,
                Id = entry.Id,
                Title = string.IsNullOrWhiteSpace(entry.Title) ? "Video " + i : entry.Title,
                Channel = string.IsNullOrWhiteSpace(entry.Channel) ? metadata.Channel : entry.Channel,
                Duration = duration,
                Thumb = string.IsNullOrWhiteSpace(entry.Thumbnail) ? Thumb(entry.Id, "mqdefault") : entry.Thumbnail
            });
            if (duration != null) total += duration.Value;
            if (i >= MaxPlaylistItems) break;
        }

        if (res.Entries.Count == 0) throw new InvalidOperationException("yt-dlp returned no available playlist entries.");

        res.Duration = total;
        // Resolve format metadata with bounded parallelism. Running one full
        // playlist extraction serially makes large playlists noticeably slower.
        // A small worker limit avoids overwhelming YouTube while keeping fetches responsive.
        using (var semaphore = new SemaphoreSlim(6, 6))
        {
            var formatTasks = res.Entries.Select(async entry =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    entry.VideoFormats = await YtDlpService.GetVideoFormatEstimatesAsync(
                        "https://www.youtube.com/watch?v=" + entry.Id, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // One unavailable/private/blocked entry must not invalidate the other videos.
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(formatTasks);
        }

        Exception? lastError = null;
        foreach (var entry in res.Entries)
        {
            try
            {
                var id = VideoId.Parse(entry.Id);
                var manifest = await Yt.Videos.Streams.GetManifestAsync(id, ct);
                var tracks = await YtDlpService.GetTracksAsync("https://www.youtube.com/watch?v=" + entry.Id, ct);
                if (entry.VideoFormats.Count == 0)
                    entry.VideoFormats = EstimateFromManifest(manifest, entry.Duration);
                res.VideoId = entry.Id;
                res.Thumb = entry.Thumb;
                res.Manifest = manifest;
                res.Tracks = tracks;
                return res;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lastError = ex; }
        }

        throw new InvalidOperationException("Playlist entries were found, but no available video could be used to load quality and track information. " + lastError?.Message);
    }

    static List<YtDlpService.VideoFormatEstimate> EstimateFromManifest(StreamManifest manifest, TimeSpan? duration)
    {
        double seconds = duration?.TotalSeconds ?? 0;
        var audio = StreamPicker.Audio(manifest, preferMp4: true);
        long audioBytes = audio?.Size.Bytes ?? 0;
        if (audioBytes <= 0 && seconds > 0 && audio != null)
            audioBytes = (long)(audio.Bitrate.BitsPerSecond / 8d * seconds);

        return manifest.GetVideoOnlyStreams()
            .GroupBy(stream => stream.VideoQuality.MaxHeight)
            .Select(group =>
            {
                var stream = group.OrderByDescending(item => item.Bitrate.BitsPerSecond).First();
                long videoBytes = stream.Size.Bytes;
                if (videoBytes <= 0 && seconds > 0)
                    videoBytes = (long)(stream.Bitrate.BitsPerSecond / 8d * seconds);
                return new YtDlpService.VideoFormatEstimate
                {
                    Height = group.Key,
                    EstimatedBytes = videoBytes + audioBytes
                };
            })
            .Where(item => item.Height > 0 && item.EstimatedBytes > 0)
            .ToList();
    }

}

public static class StreamPicker
{
    /// <summary>Video-only stream with exactly this height (prefers MP4/H.264), or null.</summary>
    public static IVideoStreamInfo? Exact(StreamManifest m, int height) =>
        m.GetVideoOnlyStreams()
         .Where(s => s.VideoQuality.MaxHeight == height)
         .OrderByDescending(s => s.Container.Name == "mp4")
         .ThenByDescending(s => s.VideoQuality.Framerate)
         .ThenByDescending(s => s.Bitrate.BitsPerSecond)
         .FirstOrDefault();

    /// <summary>Exact height if available, otherwise the closest lower one, otherwise the lowest.</summary>
    public static IVideoStreamInfo? Pick(StreamManifest m, int height)
    {
        var all = m.GetVideoOnlyStreams().ToList();
        if (all.Count == 0) return null;
        var heights = all.Select(s => s.VideoQuality.MaxHeight).Distinct().OrderBy(h => h).ToList();
        int target = heights.Where(h => h <= height).DefaultIfEmpty(heights[0]).Max();
        return Exact(m, target);
    }

    public static IAudioStreamInfo? Audio(StreamManifest m, bool preferMp4)
    {
        // Keep the order supplied by YouTube instead of sorting every audio track
        // by bitrate. On videos with alternate/dubbed tracks, the highest-bitrate
        // stream is not necessarily the original-language audio.
        var a = m.GetAudioOnlyStreams().ToList();
        if (a.Count == 0) return null;
        if (preferMp4)
        {
            var mp = a.FirstOrDefault(s => s.Container.Name == "mp4");
            if (mp != null) return mp;
        }
        return a[0];
    }
}
