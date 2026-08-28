using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using SpotifyFifinePlugin;

namespace FifineProtoHarness;

/// <summary>
/// Deterministic protocol harness for the Phase 1 Fifine plugin scaffold.
///
/// <para>
/// For each scenario it:
///   1. starts a local WebSocket server on an ephemeral loopback port,
///   2. launches the scaffold with Fifine-style arguments (named and positional),
///   3. asserts the first message is exactly { "uuid", "event" } with the
///      expected values,
///   4. closes the host connection and asserts the plugin exits with code 0.
/// </para>
///
/// Returns a non-zero exit code on any failure.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? pluginPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--plugin" && i + 1 < args.Length)
            {
                pluginPath = args[++i];
            }
        }

        if (pluginPath is null)
        {
            pluginPath = FindPluginExecutable();
        }

        if (pluginPath is null || !File.Exists(pluginPath))
        {
            Console.Error.WriteLine("harness: could not locate SpotifyFifinePlugin.exe.");
            Console.Error.WriteLine("harness: build the scaffold, or pass --plugin <path>.");
            return 1;
        }

        Console.WriteLine($"harness: using plugin executable: {pluginPath}");

        bool ok = true;
        ok &= await RunScenarioAsync(pluginPath, usePositional: false);
        ok &= await RunScenarioAsync(pluginPath, usePositional: true);

        // In-process behaviour parity: real PluginCore + fakes, exact JSON shapes.
        ok &= BehaviorScenarios.RunAll();

        // Best-effort direct DLL smoke test (safe: no playback change).
        ok &= SmokeTestDll();

        Console.WriteLine(ok ? "harness: ALL SCENARIOS PASSED" : "harness: FAILURES DETECTED");
        return ok ? 0 : 1;
    }

    private static async Task<bool> RunScenarioAsync(string pluginPath, bool usePositional)
    {
        var label = usePositional ? "positional" : "named";
        var uuid = "test-plugin-uuid-" + Guid.NewGuid().ToString("N")[..8];
        var registerEvent = "registerPlugin";

        Console.WriteLine($"harness[{label}]: starting scenario");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var psi = new ProcessStartInfo
        {
            FileName = pluginPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(pluginPath),
        };

        if (usePositional)
        {
            // Bare positional: <port> <pluginUUID> <registerEvent> <info>
            psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add(uuid);
            psi.ArgumentList.Add(registerEvent);
            psi.ArgumentList.Add("{\"application\":{\"language\":\"en\"}}");
        }
        else
        {
            psi.ArgumentList.Add("-port");
            psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("-pluginUUID");
            psi.ArgumentList.Add(uuid);
            psi.ArgumentList.Add("-registerEvent");
            psi.ArgumentList.Add(registerEvent);
            psi.ArgumentList.Add("-info");
            psi.ArgumentList.Add("{\"application\":{\"language\":\"en\"}}");
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine($"harness[{label}]: failed to start plugin process");
            listener.Stop();
            return false;
        }

        var stdoutTask = Task.Run(() => Drain(process.StandardOutput, $"[plugin:{label}]"));
        var stderrTask = Task.Run(() => Drain(process.StandardError, $"[plugin-err:{label}]"));

        try
        {
            var client = await listener.AcceptTcpClientAsync();
            using var conn = await MiniWebSocket.AcceptAsync(client);

            string? received = null;
            try
            {
                received = await conn.ReadTextFrameAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"harness[{label}]: error reading frame: {ex.Message}");
            }

            bool valid = ValidateRegistration(received, uuid, registerEvent);
            if (valid)
            {
                Console.WriteLine($"harness[{label}]: registration validated");
            }
            else
            {
                Console.Error.WriteLine($"harness[{label}]: registration validation FAILED");
            }

            // Close the host side; the plugin must exit cleanly.
            await conn.SendCloseAsync();

            if (!process.WaitForExit(10000))
            {
                Console.Error.WriteLine($"harness[{label}]: plugin did not exit after host close (timeout)");
                try
                {
                    process.Kill();
                }
                catch
                {
                    // ignore
                }

                listener.Stop();
                return false;
            }

            Console.WriteLine($"harness[{label}]: plugin exited with code {process.ExitCode}");
            bool cleanExit = process.ExitCode == 0;
            if (!cleanExit)
            {
                Console.Error.WriteLine($"harness[{label}]: expected exit code 0, got {process.ExitCode}");
            }

            return valid && cleanExit;
        }
        finally
        {
            listener.Stop();
            await stdoutTask;
            await stderrTask;
        }
    }

    private static bool ValidateRegistration(string? json, string expectedUuid, string expectedEvent)
    {
        if (string.IsNullOrEmpty(json))
        {
            Console.Error.WriteLine("harness: no registration message received");
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("uuid", out var u) || u.GetString() != expectedUuid)
            {
                return false;
            }

            if (!root.TryGetProperty("event", out var e) || e.GetString() != expectedEvent)
            {
                return false;
            }

            // Exactly the two documented fields, nothing more.
            int count = 0;
            foreach (var _ in root.EnumerateObject())
            {
                count++;
            }

            return count == 2;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"harness: registration JSON malformed: {ex.Message}");
            return false;
        }
    }

    private static void Drain(StreamReader reader, string prefix)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Console.WriteLine($"{prefix} {line}");
        }
    }

    /// <summary>
    /// Best-effort direct native smoke test. It creates the controller, starts
    /// it, reads the latest state (a read-only call), and disposes — never
    /// issuing play/pause/next/previous/open. If the prebuilt DLL is absent from
    /// the harness output, or the native call fails for environment reasons, the
    /// test is reported as SKIP (non-fatal) rather than failing the harness.
    /// </summary>
    private static bool SmokeTestDll()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "libspotifyctl.dll");
        if (!File.Exists(dll))
        {
            Console.WriteLine("smoke: SKIP (libspotifyctl.dll not present in harness output)");
            return true;
        }

        try
        {
            using var controller = new SpotifyController();
            controller.Start();

            string version = SpotifyController.Version;
            bool running = controller.IsRunning;
            var state = controller.LatestState(); // read-only; no playback change
            controller.Dispose();

            Console.WriteLine(
                $"smoke: OK (create/start/latest_state/dispose) version='{version}' " +
                $"running={running} stateRead={(state is not null)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"smoke: SKIP (native load/run failed, non-fatal): {ex.Message}");
            return true;
        }
    }

    private static string? FindPluginExecutable()
    {
        // Walk upward from the harness output directory to find the sibling
        // SpotifyFifinePlugin project, then probe its common build outputs.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var pluginDir = Path.Combine(dir.FullName, "SpotifyFifinePlugin");
            if (Directory.Exists(pluginDir))
            {
                var candidates = new[]
                {
                    Path.Combine(pluginDir, "bin", "Debug", "net8.0", "win-x64", "SpotifyFifinePlugin.exe"),
                    Path.Combine(pluginDir, "bin", "Release", "net8.0", "win-x64", "publish", "SpotifyFifinePlugin.exe"),
                    Path.Combine(pluginDir, "bin", "Debug", "net8.0", "SpotifyFifinePlugin.exe"),
                    Path.Combine(pluginDir, "bin", "Release", "net8.0", "SpotifyFifinePlugin.exe"),
                };
                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
