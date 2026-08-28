# Portfifine — native Fifine plugin

> **Phase 2: feature parity (NOT yet active).** This C# / .NET 8 NativeAOT
> scaffold now implements all seven actions, the `libspotifyctl.dll` C ABI
> wrapper, lifecycle dispatch, and Now Playing — with the **exact same Fifine
> protocol and outbound JSON shapes** as the production Node plugin. It remains
> isolated under `native/`; the active `plugins/com.wendril.spotify.sdPlugin`
> Node plugin, the manifest, the installed Fifine copy, and all assets are
> **untouched**. Switching the manifest entry point to this executable is a
> **Phase 3** task and has **not** been done.

## Layout

```text
native/
├── libspotifyctl/
│   └── libspotifyctl.dll          # prebuilt Windows x64 native asset (NO Node/koffi)
├── SpotifyFifinePlugin/          # Phase 2 feature-parity executable (net8.0, win-x64 NativeAOT)
│   ├── SpotifyFifinePlugin.csproj # NativeAOT + DLL copy-to-output/publish + InternalsVisibleTo
│   ├── Program.cs                 # launch + WebSocket connect/register + native state consumer loop
│   ├── ArgsParser.cs              # -port/-pluginUUID/-registerEvent/-info (named + positional)
│   ├── PluginOptions.cs           # resolved host args
│   ├── Protocol.cs                # JSON models + source-generated JsonSerializerContext (all outbound shapes)
│   ├── Contracts.cs               # PlaybackState model + ISpotifyController / IHostTransport boundaries
│   ├── SpotifyCtl.cs              # C-ABI P/Invoke wrapper, sequential struct, channel-fed callbacks
│   ├── PluginCore.cs              # 7 actions + lifecycle dispatch (pure logic over the two boundaries)
│   └── WebSocketTransport.cs      # WebSocket host transport (single-writer outbound channel, connect timeout)
├── Harness/                      # dependency-free deterministic harness (no NuGet packages)
│   ├── Harness.csproj             # references SpotifyFifinePlugin; carries the DLL for the smoke test
│   ├── Program.cs                 # launches the exe (registration scenarios) + DLL smoke test
│   ├── MiniWebSocket.cs           # minimal RFC 6455 server over TcpListener (BCL only)
│   ├── BehaviorScenarios.cs       # in-process exact-JSON behaviour assertions
│   ├── FakeTransport.cs           # records the EXACT production JSON
│   └── FakeController.cs          # deterministic ISpotifyController fake
└── README.md
```

No external NuGet packages are used. All JSON is `System.Text.Json` source-generated.

## Build

```text
dotnet build SpotifyFifinePlugin\SpotifyFifinePlugin.csproj
dotnet build Harness\Harness.csproj
```

## NativeAOT publish (single native .exe + the DLL beside it)

```text
dotnet publish SpotifyFifinePlugin\SpotifyFifinePlugin.csproj -c Release -r win-x64 -p:PublishAot=true
```

Output: `SpotifyFifinePlugin\bin\Release\net8.0\win-x64\publish\SpotifyFifinePlugin.exe`
plus `libspotifyctl.dll` copied next to it (verified: the prebuilt 159,232-byte DLL
lands in the publish folder).

## Run the harness

```text
dotnet run --project Harness\Harness.csproj
```

The harness validates:

1. **Process registration** (named + positional args): launches the executable,
   asserts the first frame is exactly `{ "uuid": <uuid>, "event": <registerEvent> }`,
   and that the plugin exits `0` when the host closes.
2. **In-process behaviour** using the real `PluginCore` with a `FakeTransport`
   (which serializes with the production source-gen context, so the recorded JSON
   is byte-for-byte what the socket would carry) and a `FakeController`:
   - `open` keyUp → `openUrl` with **no `context`** and `url:"spotify:"`
   - `nowplaying` willAppear → `setTitle` (`target:0,state:6`) + `setImage`
     (`data:image/jpeg;base64,…`)
   - Now Playing dedupe: identical state is silent, changed state re-emits
   - `playpause` willAppear → `setState 1`; keyUp while running → optimistic
     `setState 0` (and `pause()`)
   - not running → `showAlert` (playpause/next/previous/volup/voldown)
   - physical dispatch is **keyUp only** (keyDown is ignored)
3. **Best-effort direct DLL smoke test**: creates the controller, starts it, reads
   `latest_state` (read-only — no playback change), and disposes. If the DLL is
   absent or the native call fails for environment reasons it is reported as a
   non-fatal SKIP.

A non-zero exit code means a contract/behaviour assertion failed.

## ABI notes (validated as far as local tooling allows)

- **Exports** confirmed with `dumpbin /exports`: `spotifyctl_new`, `free`,
  `start`, `stop`, `is_running`, `play`, `pause`, `next`, `previous`, `open_uri`,
  `latest_state`, `latest_state_json`, `on_state_changed_with_replay`, `disconnect`,
  `version` are all present as unmangled `extern "C"` symbols.
- **Calling convention**: on Windows x64 there is a single native ABI; the DLL uses
  C linkage (unmangled names), so the `CallConvCdecl` mapping used for the native
  callbacks and P/Invoke is correct. (The cdecl/stdcall distinction only matters on
  x86-32, which is not a target here.)
- **Struct layout**: `spotifyctl_playback_state` is mirrored as
  `[StructLayout(LayoutKind.Sequential, Pack = 8)]` with `IntPtr` fields for the
  string/art pointers and `UIntPtr` for `size_t`. Offsets computed for x64 default
  8-byte packing: `status@0, artist@8, title@16, album@24, position_ms@32,
duration_ms@40, album_art@48, album_art_len@56, can_seek@64 … app_volume@88`.
  Strings/art are copied out **immediately** inside the native callback
  (`PtrToStringUTF8` / `Marshal.Copy`); no native memory is retained.
- **Callbacks**: `on_state_changed_with_replay` is registered once (replay variant);
  the token is retained. The native callback is a stable static
  `[UnmanagedCallersOnly]` function pointer; it pushes a managed snapshot into a
  bounded `Channel<PlaybackState>` (never writes the socket directly). All retained
  tokens are `disconnect`-ed **before** the native handle is `free`-d.

### ABI items to escalate to Oracle (if the C header disagrees)

- If the DLL was compiled with non-default `#pragma pack` the sequential offsets
  above may not match; the C header should be compared to the computed layout.
- The smoke test proves create/start/read-state/dispose work and do not alter
  playback, but it does not exercise play/pause/next/previous/open because those
  would change playback; those paths are validated logically via the harness fakes.

## Behavioural notes

- Connects to `ws://127.0.0.1:<port>` with a **5s connect timeout**; outbound writes
  go through a single reader loop so `ClientWebSocket.SendAsync` is never re-entrant.
- Sends only source-generated JSON (no runtime reflection) — NativeAOT safe.
- Lifecycle: `_willAppear`/`_willDisappear` register/clean contexts; physical
  dispatch is `keyUp` only. Now Playing polls at 1s **only while visible** and
  deduplicates via an artist/title/album/duration/art head-tail signature.
- Exits cleanly (`0`) when the host closes the connection.

## Non-goal (explicit)

This Phase 2 scaffold is **not active**. The Fifine manifest still points at the
Node plugin. No Phase 3 manifest switch, packaging, install, or benchmark has been
performed. Marketplace/SC6 publication remains out of scope per the migration plan.
