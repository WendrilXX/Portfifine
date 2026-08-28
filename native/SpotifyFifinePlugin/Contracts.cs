namespace SpotifyFifinePlugin;

/// <summary>
/// Native playback status codes (kept identical to libspotifyctl's enum so the
/// controller and action logic agree on the wire values).
/// </summary>
internal static class SpotifyStatus
{
    public const int Unknown = 0;
    public const int Stopped = 1;
    public const int Paused = 2;
    public const int Playing = 3;
    public const int ChangingTrack = 4;

    public static bool IsPlaying(int status) =>
        status == Playing || status == ChangingTrack;
}

/// <summary>
/// Managed, immutable snapshot of <c>spotifyctl_playback_state</c>. Produced by
/// copying the native struct's pointers/art immediately inside the native
/// callback (no native memory is retained past the copy).
/// </summary>
internal sealed class PlaybackState
{
    public int Status { get; init; }
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string Album { get; init; } = "";
    public long PositionMs { get; init; }
    public long DurationMs { get; init; }
    public byte[] AlbumArt { get; init; } = System.Array.Empty<byte>();
    public bool CanSeek { get; init; }
    public bool CanSkipNext { get; init; }
    public bool CanSkipPrev { get; init; }
    public bool IsAd { get; init; }
    public bool Audible { get; init; }
    public bool AppMuted { get; init; }
    public double AppVolume { get; init; }

    public bool IsPlaying => SpotifyStatus.IsPlaying(Status);
}

/// <summary>
/// Production-facing controller boundary. The real <see cref="SpotifyController"/>
/// wraps the native DLL; the harness injects a deterministic fake implementing
/// the same surface so behaviour can be asserted without touching Spotify.
/// </summary>
internal interface ISpotifyController
{
    bool IsRunning { get; }

    /// <summary>Latest known state, or <c>null</c> if unavailable.</summary>
    PlaybackState? LatestState();

    bool Play();
    bool Pause();
    bool Next();
    bool Previous();

    double GetAppVolume();
    bool TrySetAppVolume(double value);

    bool IsAppMuted();
    void SetAppMuted(bool muted);

    void OpenUri(string uri);
}

/// <summary>
/// Outbound WebSocket boundary. Implemented by the real WebSocket transport in
/// production and by a recording fake in the deterministic harness.
/// </summary>
internal interface IHostTransport
{
    void SendSetTitle(string context, string title, int target, int state);
    void SendSetImage(string context, string dataUri);
    void SendSetState(string context, int state);
    void SendShowAlert(string context);
    void SendOpenUrl(string url);
}
