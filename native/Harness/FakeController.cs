using System;
using SpotifyFifinePlugin;

namespace FifineProtoHarness;

/// <summary>
/// Deterministic <see cref="ISpotifyController"/>. The harness sets the returned
/// state and watches which transport commands the plugin issues; no Spotify
/// process is touched.
/// </summary>
internal sealed class FakeController : ISpotifyController
{
    public bool Running { get; set; } = true;
    public PlaybackState? State { get; set; }

    public bool Played { get; set; }
    public bool Paused { get; set; }
    public bool Nexted { get; set; }
    public bool Previoused { get; set; }
    public double Volume { get; set; } = 0.5;
    public bool VolumeSetSucceeds { get; set; } = true;
    public bool Muted { get; set; }
    public string? OpenedUri { get; set; }

    public bool IsRunning => Running;

    public PlaybackState? LatestState() => State;

    public bool Play()
    {
        Played = true;
        return true;
    }

    public bool Pause()
    {
        Paused = true;
        return true;
    }

    public bool Next()
    {
        Nexted = true;
        return true;
    }

    public bool Previous()
    {
        Previoused = true;
        return true;
    }

    public double GetAppVolume() => Volume;

    public bool TrySetAppVolume(double value)
    {
        if (!VolumeSetSucceeds)
            return false;

        Volume = value;
        return true;
    }

    public bool IsAppMuted() => Muted;

    public void SetAppMuted(bool muted) => Muted = muted;

    public void OpenUri(string uri) => OpenedUri = uri;

    public static PlaybackState Make(
        int status,
        string artist = "",
        string title = "",
        string album = "",
        long durationMs = 0,
        byte[]? art = null)
    {
        return new PlaybackState
        {
            Status = status,
            Artist = artist,
            Title = title,
            Album = album,
            DurationMs = durationMs,
            AlbumArt = art ?? Array.Empty<byte>(),
        };
    }
}
