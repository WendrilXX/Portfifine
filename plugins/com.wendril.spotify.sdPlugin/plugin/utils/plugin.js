"use strict";

// Minimal StreamDock/Elgato WebSocket plugin helper (CommonJS).
// Based on the proven argv layout used by Fifine/Mirabox Node plugins:
//   argv[3] = port, argv[5] = pluginUUID, argv[7] = registerEvent, argv[9] = info JSON
const WebSocket = require("ws");

class Plugins {
  constructor() {
    this.port = process.argv[3];
    this.pluginUUID = process.argv[5];
    this.registerEvent = process.argv[7];
    this.info = process.argv[9] ? JSON.parse(process.argv[9]) : {};
    this.language =
      (this.info.application && this.info.application.language) || "en";
    this.globalSettings = {};
    this.ws = null;
  }

  connect() {
    this.ws = new WebSocket("ws://127.0.0.1:" + this.port);
    this.ws.on("open", () => {
      this.ws.send(
        JSON.stringify({ uuid: this.pluginUUID, event: this.registerEvent }),
      );
      console.log("[spotify] connected to StreamDock on port " + this.port);
    });
    this.ws.on("message", (raw) => {
      let data;
      try {
        data = JSON.parse(raw.toString());
      } catch (e) {
        return;
      }
      this._handle(data);
    });
    this.ws.on("close", () => {
      console.log("[spotify] websocket closed, exiting");
      process.exit(0);
    });
    this.ws.on("error", (err) => {
      console.error("[spotify] websocket error:", err && err.message);
    });
  }

  _handle(data) {
    const action = data.action ? data.action.split(".").pop() : null;
    const map = this.actions || {};
    if (
      action &&
      map[action] &&
      typeof map[action][data.event] === "function"
    ) {
      try {
        map[action][data.event](data);
      } catch (e) {
        console.error("[spotify] action error", action, data.event, e);
      }
      return;
    }
    if (typeof this[data.event] === "function") {
      try {
        this[data.event](data);
      } catch (e) {
        console.error("[spotify] event error", data.event, e);
      }
    }
  }

  // ---- Stream Deck / StreamDock API helpers ----
  setTitle(context, str, row = 0, num = 6) {
    this._send({
      event: "setTitle",
      context,
      payload: { title: str, target: row, state: num },
    });
  }
  setImage(context, url) {
    this._send({ event: "setImage", context, payload: { image: url } });
  }
  setState(context, state) {
    this._send({ event: "setState", context, payload: { state } });
  }
  setSettings(context, settings) {
    this._send({ event: "setSettings", context, payload: settings });
  }
  getSettings(context) {
    this._send({ event: "getSettings", context });
  }
  showAlert(context) {
    this._send({ event: "showAlert", context });
  }
  showOk(context) {
    this._send({ event: "showOk", context });
  }
  sendToPropertyInspector(context, payload) {
    this._send({ event: "sendToPropertyInspector", context, payload });
  }
  openUrl(url) {
    this._send({ event: "openUrl", payload: { url } });
  }
  getGlobalSettings() {
    this._send({ event: "getGlobalSettings", context: this.pluginUUID });
  }
  setGlobalSettings(settings) {
    this._send({
      event: "setGlobalSettings",
      context: this.pluginUUID,
      payload: settings,
    });
  }
  logMessage(message) {
    this._send({ event: "logMessage", payload: { message } });
  }

  _send(obj) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(obj));
    }
  }
}

class Actions {
  constructor(plugin) {
    this.plugin = plugin;
  }
  willAppear(data) {
    this._willAppear && this._willAppear(data);
  }
  willDisappear(data) {
    this._willDisappear && this._willDisappear(data);
  }
  propertyInspectorDidAppear(data) {
    this._propertyInspectorDidAppear && this._propertyInspectorDidAppear(data);
  }
  propertyInspectorDidDisappear(data) {
    this._propertyInspectorDidDisappear &&
      this._propertyInspectorDidDisappear(data);
  }
  didReceiveSettings(data) {
    this._didReceiveSettings && this._didReceiveSettings(data);
  }
  didReceiveGlobalSettings(data) {
    if (data && data.payload) this.plugin.globalSettings = data.payload;
  }
}

module.exports = { Plugins, Actions };
