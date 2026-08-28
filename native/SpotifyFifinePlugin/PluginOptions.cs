namespace SpotifyFifinePlugin;

/// <summary>
/// Resolved Fifine host launch parameters.
/// </summary>
internal sealed class PluginOptions
{
    public int Port { get; init; }

    public string PluginUuid { get; init; } = "";

    public string RegisterEvent { get; init; } = "";

    /// <summary>
    /// Raw <c>-info</c> JSON document from the host (validated, not yet modeled).
    /// </summary>
    public string Info { get; init; } = "{}";
}
