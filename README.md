# Portfifine

Tools for migrating compatible Elgato Stream Deck resources to Fifine Control
Deck / StreamDock, while keeping custom plugins together in one repository.

Project author: **WendrilXX**.

## Contents

- `StreamDeckPortFifine.bat`: copies compatible Stream Deck plugins and icon
  packs, installs the plugins bundled in this repository, clears the cache,
  and restarts Fifine.
- `plugins/com.wendril.spotify.sdPlugin`: custom Spotify plugin, ready to use
  with Fifine.

## Usage

1. Download or clone this repository.
2. Close Fifine Control Deck if it is open.
3. Run `StreamDeckPortFifine.bat` as administrator.
4. Open Fifine and find the actions in their corresponding category.

The script looks for the standard installation at
`%APPDATA%\HotSpot\StreamDock` and, when present, copies plugins and icon
packs from `%APPDATA%\Elgato\StreamDeck`.

## Spotify Plugin

The plugin in `plugins/com.wendril.spotify.sdPlugin` is self-contained: its
dependencies, including the required DLL, are bundled in `plugin/node_modules`.
Do not delete that folder.

Features:

- Play / Pause;
- next and previous track;
- Spotify application volume in 5% increments;
- Now Playing with artist, title, and album artwork, refreshed automatically
  while visible.

It controls the local Spotify for Windows application through Windows SMTC and
Core Audio. It does not use OAuth, the Web API, or Spotify Premium. Spotify
must be open for the actions to work.

## Compatibility

- Windows 10 or later, x64;
- Fifine Control Deck / StreamDock with bundled Node.js 20;
- Spotify for Windows.

Recent official Elgato plugins whose `manifest.json` is encrypted are not
compatible with Fifine and cannot be migrated by simply copying them. The
Spotify plugin in this repository was built specifically for the
Fifine/Mirabox plugin format.
