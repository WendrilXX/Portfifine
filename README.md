# Portfifine

**Migrate compatible Stream Deck resources to Fifine Control Deck and install a local native Spotify controller.**

Portfifine is a Windows utility for Fifine Control Deck / StreamDock. It can
migrate compatible Elgato plugins and icon packs, install the bundled Spotify
plugin, clear the Fifine cache, and restart the application in one run. The
Spotify plugin is a Windows x64 C# .NET 8 NativeAOT executable.

> **Author:** [WendrilXX](https://github.com/WendrilXX)

## What is included

| Component                                                                        | Purpose                                            |
| -------------------------------------------------------------------------------- | -------------------------------------------------- |
| [`StreamDeckPortFifine.bat`](./StreamDeckPortFifine.bat)                         | Self-elevating migration and installer script.     |
| [`plugins/com.wendril.spotify.sdPlugin`](./plugins/com.wendril.spotify.sdPlugin) | Self-contained Spotify controller for Fifine.      |
| [`native`](./native)                                                             | Open C# source and deterministic protocol harness. |

## Quick start

1. Download or clone this repository.
2. Make sure **Fifine Control Deck** has been installed and opened at least once.
3. Right-click `StreamDeckPortFifine.bat` and select **Run as administrator**.
4. Wait for the script to finish. It restarts Fifine automatically.
5. Open Fifine and look for the **Spotify** category in the action list.

The script is safe to run again when this repository receives an update.

## What the script does

The installer checks that Fifine exists at `%APPDATA%\HotSpot\StreamDock`, then:

1. Copies compatible Elgato plugins when `%APPDATA%\Elgato\StreamDeck\Plugins` exists.
2. Copies compatible Elgato icon packs when `%APPDATA%\Elgato\StreamDeck\IconPacks` exists.
3. Installs `.streamDeckIconPack` files found beside the script or on the Desktop.
4. Installs every bundled `.sdPlugin` folder from this repository.
5. Clears Fifine's StoreCache and restarts Fifine Control Deck.

> You do **not** need Elgato Stream Deck installed to use the bundled Spotify plugin. The Elgato migration steps are simply skipped when those folders do not exist.

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
- Administrator permission when running the migration script.

Developers additionally need the .NET 8 SDK and Visual Studio 2022 Build Tools
with the C++ desktop workload to publish NativeAOT binaries.

## Compatibility and limitations

- The Spotify plugin is built specifically for the Fifine/Mirabox StreamDock plugin format.
- Only standard, unencrypted Elgato resources can be copied directly.
- Recent official Elgato plugins with an encrypted `manifest.json` cannot be migrated by copying their files.
- The plugin controls the Spotify **desktop application**. It does not control the web player.

## Troubleshooting

### Spotify actions do not appear

1. Run `StreamDeckPortFifine.bat` again as administrator.
2. Confirm the plugin folder exists at `%APPDATA%\HotSpot\StreamDock\plugins\com.wendril.spotify.sdPlugin`.
3. Fully close and reopen Fifine Control Deck.

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
`StreamDeckPortFifine.bat` again as administrator. The bundled plugin is
copied over the installed version and Fifine is restarted.
