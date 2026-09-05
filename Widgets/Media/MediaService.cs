using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace WDI.Widgets.Media;

public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task InitializeAsync() => _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    public GlobalSystemMediaTransportControlsSession? GetCurrentSession() => _manager?.GetCurrentSession();

    public async Task<MediaInfo?> GetCurrentMediaInfoAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        var playbackInfo = session.GetPlaybackInfo();
        return new MediaInfo
        {
            Title = properties.Title,
            Artist = properties.Artist,
            Album = properties.AlbumTitle,
            IsPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
        };
    }

    public async Task<bool> PlayAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return false;
        return await session.TryPlayAsync();
    }

    public async Task<bool> PauseAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return false;
        return await session.TryPauseAsync();
    }

    public async Task<bool> SkipNextAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return false;
        return await session.TrySkipNextAsync();
    }

    public async Task<bool> SkipPreviousAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return false;
        return await session.TrySkipPreviousAsync();
    }
}
