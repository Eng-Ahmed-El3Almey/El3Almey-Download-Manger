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

        if (pid != null && !forceSingle)
        {
            try { return await ResolvePlaylist(pid.Value, vid, ct); }
            catch (OperationCanceledException) { throw; }
            catch when (vid != null) { /* e.g. auto-generated mix: fall back to the single video */ }
        }
        if (vid == null) throw new InvalidOperationException("Unsupported link");
        return await ResolveVideo(vid.Value, ct);
    }

    static async Task<ResolveResult> ResolveVideo(VideoId id, CancellationToken ct)
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

    static async Task<ResolveResult> ResolvePlaylist(PlaylistId pid, VideoId? currentVideo, CancellationToken ct)
    {
        var playlist = await Yt.Playlists.GetAsync(pid, ct);
        var res = new ResolveResult
        {
            IsPlaylist = true,
            PlaylistTitle = playlist.Title,
            Title = playlist.Title,
            Channel = playlist.Author?.ChannelTitle ?? "",
            SingleVideoId = currentVideo?.Value
        };

        int i = 0;
        TimeSpan total = TimeSpan.Zero;
        await foreach (var v in Yt.Playlists.GetVideosAsync(pid, ct))
        {
            i++;
            res.Entries.Add(new EntryInfo
            {
                Index = i,
                Id = v.Id.Value,
                Title = v.Title,
                Channel = v.Author.ChannelTitle,
                Duration = v.Duration,
                Thumb = Thumb(v.Id.Value, "mqdefault")
            });
            if (v.Duration != null) total += v.Duration.Value;
            if (i >= MaxPlaylistItems) break;
        }
        if (res.Entries.Count == 0) throw new InvalidOperationException("Empty playlist");

        res.Duration = total;
        res.VideoId = res.Entries[0].Id;
        res.Thumb = Thumb(res.VideoId);
        // availability of qualities is taken from the first video of the playlist
        res.Manifest = await Yt.Videos.Streams.GetManifestAsync(VideoId.Parse(res.VideoId), ct);
        res.Tracks = await YtDlpService.GetTracksAsync("https://www.youtube.com/watch?v=" + res.VideoId, ct);
        return res;
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
