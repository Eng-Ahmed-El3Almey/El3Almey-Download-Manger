using LibVLCSharp.Shared;

namespace El3Almey.Services;

public sealed class VideoPlaybackService : IDisposable
{
    public LibVLC Engine { get; }
    public MediaPlayer Player { get; }

    public VideoPlaybackService()
    {
        Core.Initialize();
        Engine = new LibVLC("--no-video-title-show");
        Player = new MediaPlayer(Engine);
    }

    public void PlayFile(string path)
    {
        using var media = new Media(Engine, path, FromType.FromPath);
        Player.Play(media);
    }

    public void PlayUrl(string url)
    {
        using var media = new Media(Engine, url, FromType.FromLocation);
        Player.Play(media);
    }

    public void Dispose()
    {
        Player.Stop();
        Player.Dispose();
        Engine.Dispose();
    }
}
