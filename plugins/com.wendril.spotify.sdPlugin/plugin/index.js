"use strict";

const { Plugins, Actions } = require("./utils/plugin");
const { SpotifyClient } = require("libspotifyctl");

const plugin = new Plugins();
const actions = new Actions(plugin);

// ---- Shared Spotify client (single instance) ----
let client = null;
const playpauseContexts = new Set();
const nowplayingContexts = new Set();
const nowplayingVisuals = new Map();
let nowplayingTimer = null;

function ensureClient() {
  if (client) return client;
  client = new SpotifyClient();
  client.on("stateChanged", (state) => {
    refresh(state);
  });
  try {
    client.start();
  } catch (e) {
    console.error("[spotify] failed to start client:", e);
  }
  return client;
}

function isPlaying(state) {
  return (
    state &&
    (state.statusName === "PLAYING" || state.statusName === "CHANGING_TRACK")
  );
}

function refresh(state) {
  if (!state) return;
  // Play/Pause button state
  const playing = isPlaying(state);
  playpauseContexts.forEach((ctx) => plugin.setState(ctx, playing ? 1 : 0));
  // Now Playing title + art
  nowplayingContexts.forEach((ctx) => updateNowPlaying(ctx, state));
}

function nowPlayingSignature(state) {
  const art = state.albumArt;
  const artKey =
    art && art.length
      ? `${art.length}:${art.subarray(0, 16).toString("base64")}:${art
          .subarray(-16)
          .toString("base64")}`
      : "";

  return [
    state.artist || "",
    state.title || "",
    state.album || "",
    state.durationMs || 0,
    artKey,
  ].join("\u0000");
}

function updateNowPlaying(ctx, state, force = false) {
  const signature = nowPlayingSignature(state);
  if (!force && nowplayingVisuals.get(ctx) === signature) return;

  nowplayingVisuals.set(ctx, signature);
  const artist = state.artist || "";
  const title = state.title || "";
  const line =
    artist && title ? artist + "\n" + title : title || artist || "Spotify";
  plugin.setTitle(ctx, line, 0, 6);
  if (state.albumArt && state.albumArt.length) {
    const uri = "data:image/jpeg;base64," + state.albumArt.toString("base64");
    plugin.setImage(ctx, uri);
  }
}

function refreshNowPlaying() {
  if (!nowplayingContexts.size) return;

  const c = ensureClient();
  const state = c.latestState ? c.latestState() : null;
  if (!state) return;

  nowplayingContexts.forEach((ctx) => updateNowPlaying(ctx, state));
}

function startNowPlayingRefresh() {
  if (nowplayingTimer || !nowplayingContexts.size) return;

  const poll = () => {
    nowplayingTimer = null;
    if (!nowplayingContexts.size) return;

    refreshNowPlaying();
    nowplayingTimer = setTimeout(poll, 1000);
  };

  poll();
}

function stopNowPlayingRefresh() {
  if (nowplayingTimer) clearTimeout(nowplayingTimer);
  nowplayingTimer = null;
}

function guardRunning(c) {
  if (!c || !c.isRunning) {
    return false;
  }
  return true;
}

// ---- Actions ----
actions.playpause = {
  _willAppear(data) {
    const ctx = data.context;
    playpauseContexts.add(ctx);
    const c = ensureClient();
    const st = c.latestState ? c.latestState() : null;
    if (st) plugin.setState(ctx, isPlaying(st) ? 1 : 0);
  },
  _willDisappear(data) {
    playpauseContexts.delete(data.context);
  },
  keyDown(data) {
    const c = ensureClient();
    if (!guardRunning(c)) {
      plugin.showAlert(data.context);
      return;
    }
    try {
      const st = c.latestState ? c.latestState() : null;
      const playing = isPlaying(st);
      if (playing) c.pause();
      else c.play();
      plugin.setState(data.context, playing ? 0 : 1);
    } catch (e) {
      console.error("[spotify] playpause error", e);
      plugin.showAlert(data.context);
    }
  },
};

actions.next = {
  _willAppear(data) {
    ensureClient();
  },
  keyDown(data) {
    const c = ensureClient();
    if (!guardRunning(c)) {
      plugin.showAlert(data.context);
      return;
    }
    try {
      if (!c.next()) plugin.showAlert(data.context);
    } catch (e) {
      console.error(e);
      plugin.showAlert(data.context);
    }
  },
};

actions.previous = {
  _willAppear(data) {
    ensureClient();
  },
  keyDown(data) {
    const c = ensureClient();
    if (!guardRunning(c)) {
      plugin.showAlert(data.context);
      return;
    }
    try {
      if (!c.previous()) plugin.showAlert(data.context);
    } catch (e) {
      console.error(e);
      plugin.showAlert(data.context);
    }
  },
};

actions.volup = {
  _willAppear(data) {
    ensureClient();
  },
  keyDown(data) {
    const c = ensureClient();
    if (!guardRunning(c)) {
      plugin.showAlert(data.context);
      return;
    }
    try {
      let v = c.appVolume;
      if (typeof v !== "number" || v < 0) v = 0.5;
      v = Math.min(1, v + 0.05);
      c.appVolume = v;
    } catch (e) {
      console.error(e);
      plugin.showAlert(data.context);
    }
  },
};

actions.voldown = {
  _willAppear(data) {
    ensureClient();
  },
  keyDown(data) {
    const c = ensureClient();
    if (!guardRunning(c)) {
      plugin.showAlert(data.context);
      return;
    }
    try {
      let v = c.appVolume;
      if (typeof v !== "number" || v < 0) v = 0.5;
      v = Math.max(0, v - 0.05);
      c.appVolume = v;
    } catch (e) {
      console.error(e);
      plugin.showAlert(data.context);
    }
  },
};

actions.nowplaying = {
  _willAppear(data) {
    const ctx = data.context;
    nowplayingContexts.add(ctx);
    nowplayingVisuals.delete(ctx);
    const c = ensureClient();
    const st = c.latestState ? c.latestState() : null;
    if (st) updateNowPlaying(ctx, st, true);
    startNowPlayingRefresh();
  },
  _willDisappear(data) {
    nowplayingContexts.delete(data.context);
    nowplayingVisuals.delete(data.context);
    if (!nowplayingContexts.size) stopNowPlayingRefresh();
  },
  keyDown(data) {
    // This is a display action; a press just requests an immediate refresh.
    refreshNowPlaying();
  },
};

plugin.actions = actions; // expose actions map to dispatcher

plugin.connect();
