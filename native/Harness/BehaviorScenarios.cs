using System;
using System.Collections.Generic;
using System.Text.Json;
using SpotifyFifinePlugin;

namespace FifineProtoHarness;

/// <summary>
/// In-process behaviour tests. Each scenario builds a real <see cref="PluginCore"/>
/// with a <see cref="FakeTransport"/> (records the EXACT production JSON) and a
/// <see cref="FakeController"/>, feeds Fifine-style messages, and asserts the
/// precise outbound shapes.
/// </summary>
internal static class BehaviorScenarios
{
    private const string AOpen = "com.wendril.spotify.sdPlugin.open";
    private const string APlayPause = "com.wendril.spotify.sdPlugin.playpause";
    private const string ANext = "com.wendril.spotify.sdPlugin.next";
    private const string APrevious = "com.wendril.spotify.sdPlugin.previous";
    private const string AVolUp = "com.wendril.spotify.sdPlugin.volup";
    private const string AVolDown = "com.wendril.spotify.sdPlugin.voldown";
    private const string ANowPlaying = "com.wendril.spotify.sdPlugin.nowplaying";

    public static bool RunAll()
    {
        bool ok = true;
        ok &= Run("open keyUp => openUrl(spotify:) no context", ScenarioOpen);
        ok &= Run("nowplaying willAppear => setTitle + setImage", ScenarioNowPlayingAppear);
        ok &= Run("nowplaying dedup (same state silent, changed state emits)", ScenarioNowPlayingDedup);
        ok &= Run("playpause willAppear+keyUp => setState 1 then 0 (optimistic)", ScenarioPlayPause);
        ok &= Run("playpause keyUp when not running => showAlert", ScenarioAlertRunning);
        ok &= Run("physical dispatch is keyUp only (keyDown ignored)", ScenarioKeyUpOnly);
        ok &= Run("next/previous/volup/voldown when not running => showAlert", ScenarioTransportAlerts);
        ok &= Run("next keyUp when running => Next() no alert", ScenarioNextRunning);
        return ok;
    }

    private static bool Run(string name, Func<bool> scenario)
    {
        bool pass;
        try
        {
            pass = scenario();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"behavior[{name}]: EXCEPTION {ex}");
            pass = false;
        }

        Console.WriteLine(pass ? $"behavior[{name}]: PASS" : $"behavior[{name}]: FAIL");
        return pass;
    }

    // --- scenarios --------------------------------------------------------

    private static bool ScenarioOpen()
    {
        var t = new FakeTransport();
        var c = new FakeController();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(AOpen, "keyUp", "ctx-open"));

        if (!TryFind(t.Out, "openUrl", out var el))
            return false;
        if (el.GetProperty("payload").GetProperty("url").GetString() != "spotify:")
            return false;
        // Must NOT carry a context field.
        return !el.TryGetProperty("context", out _);
    }

    private static bool ScenarioNowPlayingAppear()
    {
        var c = new FakeController
        {
            State = FakeController.Make(SpotifyStatus.Playing, "Artist", "Title", "Album", 123, new byte[] { 1, 2, 3, 4, 5 }),
        };
        var t = new FakeTransport();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(ANowPlaying, "willAppear", "ctx-np"));

        if (!TryFind(t.Out, "setTitle", out var te))
            return false;
        if (te.GetProperty("context").GetString() != "ctx-np")
            return false;
        if (te.GetProperty("payload").GetProperty("title").GetString() != "Artist\nTitle")
            return false;
        if (te.GetProperty("payload").GetProperty("target").GetInt32() != 0)
            return false;
        if (te.GetProperty("payload").GetProperty("state").GetInt32() != 6)
            return false;

        if (!TryFind(t.Out, "setImage", out var ie))
            return false;
        if (ie.GetProperty("context").GetString() != "ctx-np")
            return false;
        var image = ie.GetProperty("payload").GetProperty("image").GetString();
        return image is not null && image.StartsWith("data:image/jpeg;base64,");
    }

    private static bool ScenarioNowPlayingDedup()
    {
        var c = new FakeController
        {
            State = FakeController.Make(SpotifyStatus.Playing, "Artist", "Title", "Album", 123, new byte[] { 9, 9, 9 }),
        };
        var t = new FakeTransport();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(ANowPlaying, "willAppear", "ctx-np"));
        int afterAppear = Count(t.Out, "setTitle");
        int afterImage = Count(t.Out, "setImage");

        // Identical poll must be silent (dedup).
        core.PollNowPlaying();
        if (Count(t.Out, "setTitle") != afterAppear || Count(t.Out, "setImage") != afterImage)
            return false;

        // Changed state must emit again.
        c.State = FakeController.Make(SpotifyStatus.Playing, "Artist2", "Title2", "Album2", 456, new byte[] { 9, 9, 9 });
        core.PollNowPlaying();
        return Count(t.Out, "setTitle") == afterAppear + 1 && Count(t.Out, "setImage") == afterImage + 1;
    }

    private static bool ScenarioPlayPause()
    {
        var c = new FakeController { State = FakeController.Make(SpotifyStatus.Playing) };
        var t = new FakeTransport();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(APlayPause, "willAppear", "ctx-pp"));
        if (!TryFind(t.Out, "setState", out var s1))
            return false;
        if (s1.GetProperty("payload").GetProperty("state").GetInt32() != 1)
            return false;

        core.HandleHostMessage(Msg(APlayPause, "keyUp", "ctx-pp"));
        // Last setState must be 0 (optimistic pause).
        var states = FindAll(t.Out, "setState");
        int last = states[^1].GetProperty("payload").GetProperty("state").GetInt32();
        if (last != 0)
            return false;
        if (!c.Paused)
            return false;
        // No alert on the happy path.
        return !TryFind(t.Out, "showAlert", out _);
    }

    private static bool ScenarioAlertRunning()
    {
        var c = new FakeController { Running = false, State = FakeController.Make(SpotifyStatus.Paused) };
        var t = new FakeTransport();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(APlayPause, "keyUp", "ctx-pp"));

        if (!TryFind(t.Out, "showAlert", out var a))
            return false;
        if (a.GetProperty("context").GetString() != "ctx-pp")
            return false;
        if (c.Paused || c.Played)
            return false;
        return Count(t.Out, "setState") == 0;
    }

    private static bool ScenarioKeyUpOnly()
    {
        var t = new FakeTransport();
        var c = new FakeController();
        using var core = new PluginCore(t, c, enableAutoPoll: false);

        // keyDown must be ignored entirely.
        core.HandleHostMessage(Msg(AOpen, "keyDown", "ctx"));
        if (Count(t.Out, "openUrl") != 0)
            return false;

        core.HandleHostMessage(Msg(AOpen, "keyUp", "ctx"));
        return Count(t.Out, "openUrl") == 1;
    }

    private static bool ScenarioTransportAlerts()
    {
        bool ok = true;
        foreach (var (action, ctx) in new[] { (ANext, "c-n"), (APrevious, "c-p"), (AVolUp, "c-u"), (AVolDown, "c-d") })
        {
            var c = new FakeController { Running = false };
            var t = new FakeTransport();
            using var core = new PluginCore(t, c, enableAutoPoll: false);
            core.HandleHostMessage(Msg(action, "keyUp", ctx));

            if (!TryFind(t.Out, "showAlert", out var a) || a.GetProperty("context").GetString() != ctx)
                ok = false;
        }

        return ok;
    }

    private static bool ScenarioNextRunning()
    {
        var c = new FakeController { Running = true, State = FakeController.Make(SpotifyStatus.Playing) };
        var t = new FakeTransport();
        using var core = new PluginCore(t, c, enableAutoPoll: false);
        core.HandleHostMessage(Msg(ANext, "keyUp", "ctx-n"));

        if (!c.Nexted)
            return false;
        if (TryFind(t.Out, "showAlert", out _))
            return false;
        return Count(t.Out, "showAlert") == 0;
    }

    // --- helpers ----------------------------------------------------------

    private static HostMessage Msg(string action, string ev, string ctx) =>
        new() { Action = action, Event = ev, Context = ctx };

    private static bool TryFind(IReadOnlyList<string> outMsgs, string name, out JsonElement elem)
    {
        foreach (var json in outMsgs)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("event", out var e) &&
                e.GetString() == name)
            {
                elem = root.Clone();
                return true;
            }
        }

        elem = default;
        return false;
    }

    private static List<JsonElement> FindAll(IReadOnlyList<string> outMsgs, string name)
    {
        var list = new List<JsonElement>();
        foreach (var json in outMsgs)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("event", out var e) &&
                e.GetString() == name)
            {
                list.Add(root.Clone());
            }
        }

        return list;
    }

    private static int Count(IReadOnlyList<string> outMsgs, string name)
    {
        int n = 0;
        foreach (var json in outMsgs)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("event", out var e) &&
                e.GetString() == name)
            {
                n++;
            }
        }

        return n;
    }
}
