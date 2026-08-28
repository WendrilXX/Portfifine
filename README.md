# Portfifine

**Migrate compatible Stream Deck resources to Fifine Control Deck and install a local native Spotify controller.**

Portfifine is a Windows utility for Fifine Control Deck / StreamDock. It can
migrate compatible Elgato plugins and icon packs, install the bundled Spotify
plugin, clear the Fifine cache, and restart the application in one run. The
Spotify plugin is a Windows x64 C# .NET 8 NativeAOT executable.

> **Author:** [WendrilXX](https://github.com/WendrilXX)

## What is included

| Component                                                                        | Purpose                                                 |
| -------------------------------------------------------------------------------- | ------------------------------------------------------- |
| [`StreamDeckPortFifine.bat`](./StreamDeckPortFifine.bat)                         | Minimal launcher (no admin required) for the installer. |
| [`scripts/Install-PortFifine.ps1`](./scripts/Install-PortFifine.ps1)             | Self-contained PowerShell installer/manager.            |
| [`plugins/com.wendril.spotify.sdPlugin`](./plugins/com.wendril.spotify.sdPlugin) | Self-contained Spotify controller for Fifine.           |
| [`native`](./native)                                                             | Open C# source and deterministic protocol harness.      |

## Quick start

1. Download or clone this repository.
2. Make sure **Fifine Control Deck** has been installed and opened at least once.
3. Run `StreamDeckPortFifine.bat` (double-click is fine; **administrator is not required**).
4. Wait for the installer to finish. It restarts Fifine automatically unless you pass `-NoRestart`.
5. Open Fifine and look for the **Spotify** category in the action list.

Optional flags can be passed to the launcher, for example:

```text
StreamDeckPortFifine.bat -BundledOnly -NoRestart
StreamDeckPortFifine.bat -Inspect
```

See [Command-line options](#command-line-options) for details. The installer is safe to run again whenever this repository receives an update.

## What the script does

The launcher forwards its arguments to `scripts/Install-PortFifine.ps1`, which checks that Fifine exists at `%APPDATA%\HotSpot\StreamDock` and ensures its `plugins` and `icons` folders are present, then:

1. Unless `-BundledOnly`: copies compatible Elgato plugins from `%APPDATA%\Elgato\StreamDeck\Plugins` when it exists. Each `.sdPlugin` is validated; if its `manifest.json` is missing, invalid, or encrypted, that plugin is **skipped with a warning** instead of being copied.
2. Unless `-BundledOnly`: copies Elgato icon packs from `%APPDATA%\Elgato\StreamDeck\IconPacks` when it exists.
3. Installs `.streamDeckIconPack` files found beside the repository (and on the Desktop unless `-NoDesktopIconPacks`). Each pack is extracted to a temporary folder, verified to contain a `.sdIconPack` directory, installed into Fifine's icons, and the temporary files are always removed.
4. Installs every bundled `.sdPlugin` folder from `plugins\` that has a valid JSON manifest, using a managed mirror so stale runtime files are removed on upgrades.
5. Clears `StoreCache.json` and restarts Fifine Control Deck unless `-NoRestart`.

> You do **not** need Elgato Stream Deck installed to use the bundled Spotify plugin. The Elgato migration steps are simply skipped when those folders do not exist.

## Command-line options

The installer accepts the following switches (passed through the launcher):

| Flag                  | Effect                                                                                                                      |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `-BundledOnly`        | Skip Elgato plugin and icon-pack migration. Only bundled items install.                                                     |
| `-NoRestart`          | Do not restart Fifine Control Deck after installing.                                                                        |
| `-NoDesktopIconPacks` | Do not scan the Desktop for `.streamDeckIconPack` files.                                                                    |
| `-NoPause`            | Do not pause at the end. Interactive runs keep the result visible until a key is pressed; use `-NoPause` for automation/CI. |
| `-Help`               | Show the built-in Portuguese quick manual.                                                                                  |
| `-Diagnose`           | Read-only check of Fifine/Elgato paths, installed plugin/profile counts, and the Fifine executable.                         |
| `-Scan`               | Read-only list of compatible Elgato plugins, bundled plugins, and available icon packs.                                     |
| `-Services`           | Read-only list of related Windows services and running processes.                                                           |
| `-Inspect`            | Run `-Diagnose`, `-Scan`, and `-Services` together (recommended first troubleshooting step).                                |

When launched interactively (double-click), the launcher keeps the result visible until a key is pressed. The PowerShell installer also accepts `-NoPause` (no effect on install logic) so the flag can be forwarded safely through the launcher for automation.

The diagnostic options are **read-only**: they do not install, copy, delete, clear caches, alter profiles, or restart Fifine. The launcher also forwards any other arguments to the PowerShell installer. No elevated/administrator context is required.

## Spotify plugin

The bundled plugin controls the local Spotify for Windows desktop app through
Windows SMTC and Core Audio. It is fully local: **no OAuth, Web API, Spotify
Premium, account credentials, or API keys are required.** It runs as
`SpotifyFifinePlugin.exe` and does not require Node.js.

### Actions

| Action           | What it does                            | Notes                                                               |
| ---------------- | --------------------------------------- | ------------------------------------------------------------------- |
| **Open Spotify** | Opens the Spotify desktop app.          | Uses the Windows `spotify:` URI.                                    |
| **Play / Pause** | Toggles playback.                       | Spotify must be running.                                            |
| **Next**         | Skips to the next track.                | Depends on the current Spotify queue.                               |
| **Previous**     | Returns to the previous track.          | Depends on the current Spotify queue.                               |
| **Volume +**     | Raises Spotify's app volume by 5%.      | Adjusts Spotify's Windows Mixer session, not its in-app slider.     |
| **Volume −**     | Lowers Spotify's app volume by 5%.      | A track must be playing so Windows exposes Spotify's audio session. |
| **Now Playing**  | Shows artist, title, and album artwork. | Refreshes automatically while the key is visible.                   |

The plugin runs each hardware action on key release (`keyUp`), which is the event model used by Fifine Control Deck.

### Manual Spotify installation

If you only want the Spotify plugin, copy this folder:

```text
plugins\com.wendril.spotify.sdPlugin
```

to:

```text
%APPDATA%\HotSpot\StreamDock\plugins\com.wendril.spotify.sdPlugin
```

Then fully close and reopen Fifine Control Deck. Keep
`SpotifyFifinePlugin.exe`, `libspotifyctl.dll`, and
`LICENSE-libspotifyctl.txt` together inside its `plugin` folder.

## Requirements

- Windows 10 or later, 64-bit;
- Fifine Control Deck / StreamDock `3.10.188.226` or later;
- Spotify for Windows for the Spotify actions;
- **No administrator permission is required** to run the installer.

Developers additionally need the .NET 8 SDK and Visual Studio 2022 Build Tools
with the C++ desktop workload to publish NativeAOT binaries.

## Compatibility and limitations

- The Spotify plugin is built specifically for the Fifine/Mirabox StreamDock plugin format.
- Only standard, unencrypted Elgato resources can be copied directly.
- Recent official Elgato plugins with an encrypted `manifest.json` are **detected and skipped** (with a warning) rather than copied, because copying an encrypted manifest would corrupt the install.
- Fifine **profiles are never modified** by the installer; only plugin and icon data plus the store cache are touched.
- The plugin controls the Spotify **desktop application**. It does not control the web player.

## Troubleshooting

### Check the environment before installing

Run the complete read-only inspection:

```text
StreamDeckPortFifine.bat -Inspect
```

It reports the detected Fifine/Elgato folders, compatible resources, and relevant services/processes without changing anything.

### Spotify actions do not appear

1. Run `StreamDeckPortFifine.bat` again (no administrator required). If Fifine was open, let the installer restart it, or run with `-NoRestart` and reopen Fifine manually.
2. Confirm the plugin folder exists at `%APPDATA%\HotSpot\StreamDock\plugins\com.wendril.spotify.sdPlugin`.
3. Fully close and reopen Fifine Control Deck.

### A plugin was skipped during migration

- Elgato plugins or icon packs with a missing, invalid, or **encrypted** `manifest.json` are skipped on purpose. Copy those resources manually if you need them, or use the official Elgato export where available.

### Spotify actions appear but do nothing

- Use plugin version **2.0.1** or newer (shown in `manifest.json`).
- Start Spotify for Windows before using playback controls.
- For **Volume +** and **Volume −**, start playback first; a paused app may not have an active Core Audio session.
- Volume actions change Spotify's Windows Mixer volume, not the in-app Spotify slider or Windows master volume.
- Remove and add the action again in Fifine if it was placed before an update.

### Now Playing has no track details or album art

Start a track in Spotify and wait up to one second. The key refreshes on its
own while it remains visible.

## Project structure

```text
Portfifine/
├── StreamDeckPortFifine.bat
├── scripts/
│   └── Install-PortFifine.ps1     # self-contained installer
├── native/                         # C# NativeAOT source and harness
└── plugins/
    └── com.wendril.spotify.sdPlugin/
        ├── manifest.json
        ├── plugin/                 # .exe, libspotifyctl.dll, license
        └── static/                 # action icons
```

## Open source

The full plugin source is included in this repository under
[`native/SpotifyFifinePlugin`](./native/SpotifyFifinePlugin). The dependency-free
test harness in [`native/Harness`](./native/Harness) verifies the Fifine
WebSocket registration and action behavior without requiring physical hardware.

Build, publish, and test commands are documented in
[`native/README.md`](./native/README.md).

## Updating

Pull or download the latest repository version, then run
`StreamDeckPortFifine.bat` again (no administrator required). The bundled plugin
is mirrored over the installed version (stale runtime files are removed) and
Fifine is restarted unless you pass `-NoRestart`.
