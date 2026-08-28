using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyFifinePlugin;

/// <summary>
/// Phase 2 Fifine plugin entry point.
///
/// <para>
/// Retains the Phase 1 launch + WebSocket registration contract and adds the
/// full seven-action behaviour on top of <c>libspotifyctl.dll</c>:
///   - <c>open</c> keyUp =&gt; openUrl spotify:
///   - playpause / next / previous guarded by isRunning (optimistic state)
///   - volume up/down (±0.05, clamped [0,1], defaults to 0.5)
///   - nowplaying registers contexts on willAppear, emits title + art, polls
///     only while visible at 1s, deduples via head/tail signature, cleans up on
///     willDisappear
///   - lifecycle _willAppear/_willDisappear; physical dispatch is keyUp only.
/// Native state changes are pushed onto a thread-safe channel and drained by a
/// single managed loop that fans out to contexts — no WebSocket write happens on
/// the native thread.
/// </para>
/// </summary>
internal static class Program
{
    private const int ConnectTimeoutMs = 5000;

    private static int Main(string[] args)
    {
        if (!ArgsParser.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"fifine-plugin: {error}");
            Console.Error.WriteLine(
                "usage: SpotifyFifinePlugin -port <port> -pluginUUID <uuid> -registerEvent <event> [-info <json>]");
            return 1;
        }

        try
        {
            RunAsync(options).GetAwaiter().GetResult();
            return 0;
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C / host-initiated cancellation: exit cleanly.
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fifine-plugin: fatal: {ex.Message}");
            return 1;
        }
    }

    private static async Task RunAsync(PluginOptions options)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var uri = new Uri($"ws://127.0.0.1:{options.Port}");

        using var transport = new WebSocketHostTransport();
        await transport.ConnectAsync(uri, ConnectTimeoutMs, cts.Token);

        Console.WriteLine($"[plugin] connected to Fifine host on port {options.Port}");

        using var controller = new SpotifyController();
        controller.Start();

        using var pluginCore = new PluginCore(transport, controller, enableAutoPoll: true);

        // Exact Phase 1 registration shape.
        var registration = new RegisterRequest
        {
            Uuid = options.PluginUuid,
            Event = options.RegisterEvent,
        };
        transport.SendRaw(JsonSerializer.Serialize(registration, PluginJsonContext.Default.RegisterRequest));

        // Single managed consumer that fans native state changes out to contexts.
        var stateTask = ConsumeStateLoopAsync(controller.StateReader, pluginCore, cts.Token);

        try
        {
            await transport.ReceiveLoopAsync(json =>
            {
                HostMessage? message = null;
                try
                {
                    message = JsonSerializer.Deserialize(json, PluginJsonContext.Default.HostMessage);
                }
                catch (JsonException)
                {
                    return Task.CompletedTask;
                }

                if (message is not null)
                    pluginCore.HandleHostMessage(message);

                return Task.CompletedTask;
            }, cts.Token);

            Console.WriteLine("[plugin] host closed connection; exiting cleanly");
        }
        finally
        {
            // Disposing the controller completes the state channel, which lets
            // the consumer loop (stateTask) drain and exit without deadlocking.
            pluginCore.Dispose();
            controller.Dispose();
            try { await stateTask; } catch { /* already shutting down */ }
        }
    }

    private static async Task ConsumeStateLoopAsync(
        System.Threading.Channels.ChannelReader<PlaybackState> reader,
        PluginCore pluginCore,
        CancellationToken token)
    {
        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var state))
                {
                    pluginCore.OnStateChanged(state);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }
}
