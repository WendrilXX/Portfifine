using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SpotifyFifinePlugin;

/// <summary>
/// Production <see cref="IHostTransport"/> backed by a <see cref="ClientWebSocket"/>.
///
/// <para>
/// All outbound writes are funnelled through a single reader loop over a
/// <see cref="Channel{String}"/>, so <c>ClientWebSocket.SendAsync</c> is never
/// called concurrently (it is not safe to call re-entrantly). This also keeps the
/// native callback thread and the 1s Now Playing poll from racing the socket.
/// </para>
/// </summary>
internal sealed class WebSocketHostTransport : IHostTransport, IDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly Channel<string> _out =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _writerCts = new();
    private Task? _writer;

    public WebSocketHostTransport()
    {
        _ws = new ClientWebSocket();
    }

    public async Task ConnectAsync(Uri uri, int connectTimeoutMs, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        linked.CancelAfter(connectTimeoutMs);

        try
        {
            await _ws.ConnectAsync(uri, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException($"WebSocket connect to {uri} timed out after {connectTimeoutMs} ms");
        }

        _writer = Task.Run(() => WriterLoopAsync(_writerCts.Token));
    }

    private async Task WriterLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _out.Reader.WaitToReadAsync(token))
            {
                while (_out.Reader.TryRead(out var json))
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (WebSocketException)
        {
            // host already gone; nothing more to flush
        }
    }

    /// <summary>Enqueue a pre-serialized frame (used for registration).</summary>
    public void SendRaw(string json) => _out.Writer.TryWrite(json);

    /// <summary>
    /// Reads inbound text frames until the host closes the socket. Each frame is
    /// handed to <paramref name="onMessage"/> for dispatch. Returns when a Close
    /// frame is observed.
    /// </summary>
    public async Task ReceiveLoopAsync(Func<string, Task> onMessage, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await _ws.ReceiveAsync(buffer, token);

            if (result.MessageType == WebSocketMessageType.Close)
                return;

            ms.Write(buffer, 0, result.Count);

            if (!result.EndOfMessage)
                continue;

            var json = Encoding.UTF8.GetString(ms.ToArray());
            ms.SetLength(0);
            await onMessage(json);
        }
    }

    public void SendSetTitle(string context, string title, int target, int state)
    {
        var req = new SetTitleRequest
        {
            Context = context,
            Payload = new SetTitlePayload { Title = title, Target = target, State = state },
        };
        _out.Writer.TryWrite(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetTitleRequest));
    }

    public void SendSetImage(string context, string dataUri)
    {
        var req = new SetImageRequest
        {
            Context = context,
            Payload = new SetImagePayload { Image = dataUri },
        };
        _out.Writer.TryWrite(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetImageRequest));
    }

    public void SendSetState(string context, int state)
    {
        var req = new SetStateRequest
        {
            Context = context,
            Payload = new SetStatePayload { State = state },
        };
        _out.Writer.TryWrite(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetStateRequest));
    }

    public void SendShowAlert(string context)
    {
        var req = new ShowAlertRequest { Context = context };
        _out.Writer.TryWrite(JsonSerializer.Serialize(req, PluginJsonContext.Default.ShowAlertRequest));
    }

    public void SendOpenUrl(string url)
    {
        var req = new OpenUrlRequest { Payload = new OpenUrlPayload { Url = url } };
        _out.Writer.TryWrite(JsonSerializer.Serialize(req, PluginJsonContext.Default.OpenUrlRequest));
    }

    public void Dispose()
    {
        _writerCts.Cancel();

        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "plugin shutdown", CancellationToken.None)
                    .GetAwaiter().GetResult();
        }
        catch
        {
            // best-effort
        }

        _writer?.Wait(TimeSpan.FromSeconds(2));
        _writerCts.Dispose();
        _ws.Dispose();
    }
}
