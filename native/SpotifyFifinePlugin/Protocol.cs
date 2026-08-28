using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotifyFifinePlugin;

/// <summary>
/// Registration payload sent to the Fifine host immediately after the WebSocket
/// connection opens. Mirrors the Node plugin's exact <c>{ "uuid", "event" }</c> shape.
/// </summary>
internal sealed class RegisterRequest
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = "";

    [JsonPropertyName("event")]
    public string Event { get; init; } = "";
}

/// <summary>
/// A host-to-plugin message. The plugin only consumes <c>action</c>,
/// <c>event</c> and <c>context</c>; <c>payload</c> is retained as a raw
/// <see cref="JsonElement"/> for forward compatibility (source-gen friendly).
/// </summary>
internal sealed class HostMessage
{
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("context")]
    public string? Context { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}

// ---------------------------------------------------------------------------
// Outbound host commands. Every shape below is kept byte-for-byte compatible
// with the Node plugin's utils/plugin.js helpers. Source generation is used so
// no runtime reflection runs on the hot WebSocket send path (NativeAOT safe).
// ---------------------------------------------------------------------------

internal sealed class SetTitleRequest
{
    [JsonPropertyName("event")]
    public string Event { get; } = "setTitle";

    [JsonPropertyName("context")]
    public string Context { get; init; } = "";

    [JsonPropertyName("payload")]
    public SetTitlePayload Payload { get; init; } = new();
}

internal sealed class SetTitlePayload
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("target")]
    public int Target { get; init; }

    [JsonPropertyName("state")]
    public int State { get; init; }
}

internal sealed class SetImageRequest
{
    [JsonPropertyName("event")]
    public string Event { get; } = "setImage";

    [JsonPropertyName("context")]
    public string Context { get; init; } = "";

    [JsonPropertyName("payload")]
    public SetImagePayload Payload { get; init; } = new();
}

internal sealed class SetImagePayload
{
    [JsonPropertyName("image")]
    public string Image { get; init; } = "";
}

internal sealed class SetStateRequest
{
    [JsonPropertyName("event")]
    public string Event { get; } = "setState";

    [JsonPropertyName("context")]
    public string Context { get; init; } = "";

    [JsonPropertyName("payload")]
    public SetStatePayload Payload { get; init; } = new();
}

internal sealed class SetStatePayload
{
    [JsonPropertyName("state")]
    public int State { get; init; }
}

internal sealed class ShowAlertRequest
{
    [JsonPropertyName("event")]
    public string Event { get; } = "showAlert";

    [JsonPropertyName("context")]
    public string Context { get; init; } = "";
}

/// <summary>
/// <c>openUrl</c> is sent with NO context field, exactly like the Node helper.
/// </summary>
internal sealed class OpenUrlRequest
{
    [JsonPropertyName("event")]
    public string Event { get; } = "openUrl";

    [JsonPropertyName("payload")]
    public OpenUrlPayload Payload { get; init; } = new();
}

internal sealed class OpenUrlPayload
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}

/// <summary>
/// Source-generated JSON metadata. Required for NativeAOT so that no runtime
/// reflection-based serialization is used on the hot WebSocket paths.
/// </summary>
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(HostMessage))]
[JsonSerializable(typeof(SetTitleRequest))]
[JsonSerializable(typeof(SetImageRequest))]
[JsonSerializable(typeof(SetStateRequest))]
[JsonSerializable(typeof(ShowAlertRequest))]
[JsonSerializable(typeof(OpenUrlRequest))]
internal partial class PluginJsonContext : JsonSerializerContext
{
}
