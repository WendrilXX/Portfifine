using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SpotifyFifinePlugin;

/// <summary>
/// Production action + lifecycle dispatch. Pure logic: it depends only on
/// <see cref="IHostTransport"/> (outbound) and <see cref="ISpotifyController"/>
/// (Spotify), so the deterministic harness can exercise every behaviour with a
/// fake of each boundary. Faithful to <c>plugin/index.js</c> action shapes.
/// </summary>
internal sealed class PluginCore : IDisposable
{
    private readonly IHostTransport _transport;
    private readonly ISpotifyController _controller;
    private readonly bool _enableAutoPoll;
    private readonly object _gate = new();

    private readonly HashSet<string> _playpauseContexts = new();
    private readonly HashSet<string> _nowplayingContexts = new();
    private readonly Dictionary<string, string> _nowplayingVisuals = new();
    private Timer? _nowplayingTimer;
    private bool _disposed;

    public PluginCore(IHostTransport transport, ISpotifyController controller, bool enableAutoPoll = true)
    {
        _transport = transport;
        _controller = controller;
        _enableAutoPoll = enableAutoPoll;
    }

    // -----------------------------------------------------------------------
    // Host message entry point
    // -----------------------------------------------------------------------

    public void HandleHostMessage(HostMessage msg)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            HandleHostMessageCore(msg);
        }
    }

    private void HandleHostMessageCore(HostMessage msg)
    {
        var suffix = ActionSuffix(msg.Action);
        if (suffix is null)
            return;

        var context = msg.Context ?? "";
        var ev = msg.Event;

        switch (suffix)
        {
            case "open":
                if (ev == "keyUp")
                    _transport.SendOpenUrl("spotify:");
                break;

            case "playpause":
                HandlePlayPause(ev, context);
                break;

            case "next":
                if (ev == "willAppear")
                {
                    // ensures the shared controller is started (no-op once live)
                }
                else if (ev == "keyUp")
                {
                    if (!_controller.IsRunning) { _transport.SendShowAlert(context); break; }
                    if (!_controller.Next()) _transport.SendShowAlert(context);
                }
                break;

            case "previous":
                if (ev == "willAppear")
                {
                    // ensures the shared controller is started (no-op once live)
                }
                else if (ev == "keyUp")
                {
                    if (!_controller.IsRunning) { _transport.SendShowAlert(context); break; }
                    if (!_controller.Previous()) _transport.SendShowAlert(context);
                }
                break;

            case "volup":
                if (ev == "keyUp")
                    HandleVolume(context, up: true);
                break;

            case "voldown":
                if (ev == "keyUp")
                    HandleVolume(context, up: false);
                break;

            case "nowplaying":
                HandleNowPlaying(ev, context);
                break;
        }
        // keyDown and any other event are intentionally ignored: physical
        // dispatch is keyUp only.
    }

    private void HandlePlayPause(string? ev, string context)
    {
        if (ev == "willAppear")
        {
            _playpauseContexts.Add(context);
            var st = _controller.LatestState();
            if (st is not null)
                _transport.SendSetState(context, st.IsPlaying ? 1 : 0);
        }
        else if (ev == "willDisappear")
        {
            _playpauseContexts.Remove(context);
        }
        else if (ev == "keyUp")
        {
            if (!_controller.IsRunning)
            {
                _transport.SendShowAlert(context);
                return;
            }

            var st = _controller.LatestState();
            bool playing = st is not null && st.IsPlaying;
            // Optimistic toggle: act on current status, then reflect it.
            if (playing)
                _controller.Pause();
            else
                _controller.Play();

            _transport.SendSetState(context, playing ? 0 : 1);
        }
    }

    private void HandleVolume(string context, bool up)
    {
        if (!_controller.IsRunning)
        {
            _transport.SendShowAlert(context);
            return;
        }

        double v = _controller.GetAppVolume();
        if (double.IsNaN(v) || v < 0)
            v = 0.5;

        v = up ? Math.Min(1.0, v + 0.05) : Math.Max(0.0, v - 0.05);
        if (!_controller.TrySetAppVolume(v))
            _transport.SendShowAlert(context);
    }

    private void HandleNowPlaying(string? ev, string context)
    {
        if (ev == "willAppear")
        {
            _nowplayingContexts.Add(context);
            _nowplayingVisuals.Remove(context);
            var st = _controller.LatestState();
            if (st is not null)
                UpdateNowPlaying(context, st, force: true);
            StartNowPlayingRefresh();
        }
        else if (ev == "willDisappear")
        {
            _nowplayingContexts.Remove(context);
            _nowplayingVisuals.Remove(context);
            if (_nowplayingContexts.Count == 0)
                StopNowPlayingRefresh();
        }
        else if (ev == "keyUp")
        {
            // Display action: a press requests an immediate refresh.
            PollNowPlaying();
        }
    }

    // -----------------------------------------------------------------------
    // State fan-out (driven by native callback consumer / poll timer)
    // -----------------------------------------------------------------------

    public void OnStateChanged(PlaybackState? state)
    {
        lock (_gate)
        {
            if (_disposed || state is null)
                return;

            bool playing = state.IsPlaying;
            foreach (var ctx in _playpauseContexts)
                _transport.SendSetState(ctx, playing ? 1 : 0);

            foreach (var ctx in _nowplayingContexts)
                UpdateNowPlaying(ctx, state, force: false);
        }
    }

    public void PollNowPlaying()
    {
        lock (_gate)
        {
            if (_disposed || _nowplayingContexts.Count == 0)
                return;

            var state = _controller.LatestState();
            if (state is null)
                return;

            foreach (var ctx in _nowplayingContexts)
                UpdateNowPlaying(ctx, state, force: false);
        }
    }

    private void UpdateNowPlaying(string context, PlaybackState state, bool force)
    {
        string signature = NowPlayingSignature(state);
        if (!force && _nowplayingVisuals.TryGetValue(context, out var prev) && prev == signature)
            return;

        _nowplayingVisuals[context] = signature;

        string artist = state.Artist ?? "";
        string title = state.Title ?? "";
        string line = (artist.Length > 0 && title.Length > 0)
            ? artist + "\n" + title
            : (title.Length > 0 ? title : (artist.Length > 0 ? artist : "Spotify"));

        _transport.SendSetTitle(context, line, 0, 6);

        if (state.AlbumArt is { Length: > 0 })
        {
            string uri = "data:image/jpeg;base64," + Convert.ToBase64String(state.AlbumArt);
            _transport.SendSetImage(context, uri);
        }
    }

    /// <summary>
    /// Head/tail signature used to dedupe Now Playing updates. Mirrors the
    /// Node plugin: artist + title + album + duration + a 16-byte head/tail
    /// base64 of the album art, joined by NUL.
    /// </summary>
    private static string NowPlayingSignature(PlaybackState state)
    {
        var art = state.AlbumArt;
        string artKey;
        if (art is { Length: > 0 })
        {
            int headLen = Math.Min(16, art.Length);
            int tailStart = Math.Max(0, art.Length - 16);
            int tailLen = Math.Min(16, art.Length);
            string head = Convert.ToBase64String(art, 0, headLen);
            string tail = Convert.ToBase64String(art, tailStart, tailLen);
            artKey = $"{art.Length}:{head}:{tail}";
        }
        else
        {
            artKey = "";
        }

        return string.Join("\0", state.Artist ?? "", state.Title ?? "", state.Album ?? "", state.DurationMs, artKey);
    }

    private void StartNowPlayingRefresh()
    {
        if (!_enableAutoPoll || _nowplayingTimer is not null || _nowplayingContexts.Count == 0)
            return;

        _nowplayingTimer = new Timer(_ => PollNowPlaying(), null, 0, 1000);
    }

    private void StopNowPlayingRefresh()
    {
        _nowplayingTimer?.Dispose();
        _nowplayingTimer = null;
    }

    private static string? ActionSuffix(string? action)
    {
        if (string.IsNullOrEmpty(action))
            return null;

        int idx = action.LastIndexOf('.');
        return idx >= 0 ? action.Substring(idx + 1) : action;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            StopNowPlayingRefresh();
            _playpauseContexts.Clear();
            _nowplayingContexts.Clear();
            _nowplayingVisuals.Clear();
        }
    }
}
