using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FifineProtoHarness;

/// <summary>
/// Minimal dependency-free WebSocket server used to exercise the Fifine plugin
/// contract from a local host perspective. Implements just enough of RFC 6455
/// (HTTP upgrade handshake + masked client frame decode + close frame) to avoid
/// any external NuGet packages.
/// </summary>
internal sealed class MiniWebSocket : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private MiniWebSocket(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
    }

    public static async Task<MiniWebSocket> AcceptAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[4096];
        int total = 0;

        // Read HTTP request headers (terminated by a blank line).
        bool headersDone = false;
        while (!headersDone)
        {
            int n = await stream.ReadAsync(buffer, total, buffer.Length - total);
            if (n == 0)
            {
                throw new IOException("client closed before handshake");
            }

            total += n;
            for (int i = 3; i < total; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n')
                {
                    headersDone = true;
                    break;
                }
            }

            if (headersDone)
            {
                break;
            }

            if (total >= buffer.Length)
            {
                throw new IOException("handshake headers too large");
            }
        }

        var headerText = Encoding.ASCII.GetString(buffer, 0, total);
        string? key = null;
        foreach (var line in headerText.Split("\r\n"))
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
            }
        }

        if (key is null)
        {
            throw new InvalidOperationException("missing Sec-WebSocket-Key in handshake");
        }

        var accept = ComputeAccept(key);
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes);

        return new MiniWebSocket(client, stream);
    }

    public async Task<string> ReadTextFrameAsync()
    {
        var header = new byte[2];
        await FillAsync(_stream, header, 2);

        // Client frames MUST be masked (RFC 6455); server frames MUST NOT be.
        if ((header[1] & 0x80) == 0)
        {
            throw new InvalidOperationException("expected a masked client frame");
        }

        int len = header[1] & 0x7F;
        if (len == 126)
        {
            var ext = new byte[2];
            await FillAsync(_stream, ext, 2);
            len = (ext[0] << 8) | ext[1];
        }
        else if (len == 127)
        {
            var ext = new byte[8];
            await FillAsync(_stream, ext, 8);
            long big = 0;
            for (int i = 0; i < 8; i++)
            {
                big = (big << 8) | ext[i];
            }

            if (big > int.MaxValue)
            {
                throw new InvalidOperationException("frame too large for harness");
            }

            len = (int)big;
        }

        var mask = new byte[4];
        await FillAsync(_stream, mask, 4);
        var payload = new byte[len];
        await FillAsync(_stream, payload, len);
        for (int i = 0; i < len; i++)
        {
            payload[i] ^= mask[i & 3];
        }

        return Encoding.UTF8.GetString(payload);
    }

    public async Task SendCloseAsync()
    {
        // 0x88 = FIN + Close, 0x00 = no payload.
        var frame = new byte[] { 0x88, 0x00 };
        await _stream.WriteAsync(frame);
        await _stream.FlushAsync();
    }

    private static async Task FillAsync(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer, read, count - read);
            if (n == 0)
            {
                throw new IOException("connection closed while reading frame");
            }

            read += n;
        }
    }

    private static string ComputeAccept(string key)
    {
        const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(key + Guid));
        return Convert.ToBase64String(hash);
    }

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _client.Dispose();
        }
        catch
        {
            // ignore
        }
    }
}
