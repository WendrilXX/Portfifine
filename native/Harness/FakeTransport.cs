using System;
using System.Collections.Generic;
using System.Text.Json;
using SpotifyFifinePlugin;

namespace FifineProtoHarness;

/// <summary>
/// Recording <see cref="IHostTransport"/>. It serializes outbound commands with
/// the SAME source-generated context the production transport uses, so the
/// recorded JSON is exactly what the real WebSocket would carry. Assertions then
/// inspect those exact shapes.
/// </summary>
internal sealed class FakeTransport : IHostTransport
{
    private readonly object _lock = new();
    private readonly List<string> _out = new();

    public IReadOnlyList<string> Out
    {
        get
        {
            lock (_lock)
            {
                return new List<string>(_out);
            }
        }
    }

    public void SendSetTitle(string context, string title, int target, int state)
    {
        var req = new SetTitleRequest
        {
            Context = context,
            Payload = new SetTitlePayload { Title = title, Target = target, State = state },
        };
        Record(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetTitleRequest));
    }

    public void SendSetImage(string context, string dataUri)
    {
        var req = new SetImageRequest
        {
            Context = context,
            Payload = new SetImagePayload { Image = dataUri },
        };
        Record(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetImageRequest));
    }

    public void SendSetState(string context, int state)
    {
        var req = new SetStateRequest
        {
            Context = context,
            Payload = new SetStatePayload { State = state },
        };
        Record(JsonSerializer.Serialize(req, PluginJsonContext.Default.SetStateRequest));
    }

    public void SendShowAlert(string context)
    {
        var req = new ShowAlertRequest { Context = context };
        Record(JsonSerializer.Serialize(req, PluginJsonContext.Default.ShowAlertRequest));
    }

    public void SendOpenUrl(string url)
    {
        var req = new OpenUrlRequest { Payload = new OpenUrlPayload { Url = url } };
        Record(JsonSerializer.Serialize(req, PluginJsonContext.Default.OpenUrlRequest));
    }

    private void Record(string json)
    {
        lock (_lock)
        {
            _out.Add(json);
        }
    }
}
