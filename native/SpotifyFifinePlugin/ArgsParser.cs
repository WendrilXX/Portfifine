using System;
using System.Text.Json;

namespace SpotifyFifinePlugin;

/// <summary>
/// Robust parser for the Fifine host launch arguments.
///
/// <para>
/// The Fifine/Mirabox host launches plugins with named flag/value pairs, e.g.
/// <c>-port 12345 -pluginUUID &lt;uuid&gt; -registerEvent &lt;event&gt; -info {…}</c>.
/// C#'s <see cref="Main(string[])"/> receives these tokens directly (unlike Node's
/// <c>process.argv</c>, which leads with <c>[node, script]</c>).
/// </para>
///
/// <para>
/// A positional fallback is also supported for hosts that pass bare values
/// <c>&lt;port&gt; &lt;pluginUUID&gt; &lt;registerEvent&gt; &lt;info&gt;</c>, accounting for
/// the absence of the Node program-name prefix. Both forms reject missing or
/// invalid values with a descriptive stderr message and a non-zero exit.
/// </para>
/// </summary>
internal static class ArgsParser
{
    public static bool TryParse(string[] args, out PluginOptions options, out string error)
    {
        options = new PluginOptions();
        error = "";

        if (args.Length == 0)
        {
            error = "no arguments provided by host";
            return false;
        }

        // Named flags start with '-'. Bare positional values do not (and C# has
        // no program-name prefix, unlike Node process.argv).
        if (!args[0].StartsWith("-"))
        {
            return TryParsePositional(args, out options, out error);
        }

        return TryParseNamed(args, out options, out error);
    }

    private static bool TryParseNamed(string[] args, out PluginOptions options, out string error)
    {
        string? port = null;
        string? uuid = null;
        string? registerEvent = null;
        string? info = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-port":
                    port = ReadValue(args, ref i, out error);
                    if (error.Length != 0) { options = new(); return false; }
                    break;
                case "-pluginUUID":
                    uuid = ReadValue(args, ref i, out error);
                    if (error.Length != 0) { options = new(); return false; }
                    break;
                case "-registerEvent":
                    registerEvent = ReadValue(args, ref i, out error);
                    if (error.Length != 0) { options = new(); return false; }
                    break;
                case "-info":
                    info = ReadValue(args, ref i, out error);
                    if (error.Length != 0) { options = new(); return false; }
                    break;
                default:
                    // Unknown flags are tolerated for forward compatibility.
                    break;
            }
        }

        return Validate(port, uuid, registerEvent, info, out options, out error);
    }

    private static bool TryParsePositional(string[] args, out PluginOptions options, out string error)
    {
        if (args.Length < 4)
        {
            error = "positional usage requires: <port> <pluginUUID> <registerEvent> <info>";
            options = new();
            return false;
        }

        return Validate(args[0], args[1], args[2], args[3], out options, out error);
    }

    private static string? ReadValue(string[] args, ref int i, out string error)
    {
        error = "";
        if (i + 1 >= args.Length)
        {
            error = $"missing value for flag '{args[i]}'";
            return null;
        }

        i++;
        return args[i];
    }

    private static bool Validate(
        string? port,
        string? uuid,
        string? registerEvent,
        string? info,
        out PluginOptions options,
        out string error)
    {
        options = new();
        error = "";

        if (string.IsNullOrWhiteSpace(port))
        {
            error = "missing required -port";
            return false;
        }

        if (!int.TryParse(port, out var portValue) || portValue <= 0 || portValue > 65535)
        {
            error = $"invalid -port value '{port}' (must be an integer 1-65535)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uuid))
        {
            error = "missing required -pluginUUID";
            return false;
        }

        if (string.IsNullOrWhiteSpace(registerEvent))
        {
            error = "missing required -registerEvent";
            return false;
        }

        var infoValue = info ?? "{}";
        try
        {
            using var doc = JsonDocument.Parse(infoValue);
        }
        catch (JsonException)
        {
            error = $"invalid -info JSON: {infoValue}";
            return false;
        }

        options = new PluginOptions
        {
            Port = portValue,
            PluginUuid = uuid!,
            RegisterEvent = registerEvent!,
            Info = infoValue,
        };
        return true;
    }
}
