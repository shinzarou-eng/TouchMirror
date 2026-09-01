<div align="center">

## 🇫🇷 [LIRE EN FRANÇAIS — CLIQUEZ ICI](README.fr.md)

</div>

<div align="center">

<img src="assets/mascot.png" width="120" alt="TouchMirror mascot">

# TouchMirror

**Native Android mirroring for Windows — built for Dofus Touch.**

Free, open source, no account, no ads.

[![Version](https://img.shields.io/badge/version-0.4.0-D9A94E?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square)](https://github.com/shinzarou-eng/TouchMirror)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Dofus Touch](https://img.shields.io/badge/optimized%20for-Dofus%20Touch-D9A94E?style=flat-square)](https://www.dofus-touch.com)
[![Discord](https://img.shields.io/badge/Discord-join%20us-5865F2?style=flat-square)](https://discord.gg/DBJ9kNCdX)

<img src="docs/screenshot.png" width="780" alt="TouchMirror — Dofus Touch in game">

**[Download the latest release](https://github.com/shinzarou-eng/TouchMirror/releases/latest)** · [Discord](https://discord.gg/DBJ9kNCdX) · [Documentation](docs/wiki/Home.md) · [Roadmap](ROADMAP.md) · [Report a bug](https://github.com/shinzarou-eng/TouchMirror/issues) · [Suggest a feature](https://github.com/shinzarou-eng/TouchMirror/issues/new)

</div>

---

## Why TouchMirror?

TouchMirror is a **native Windows app** that displays and controls your Android phone from your PC: plug it in, click, play. Polished dark UI, low latency, multiple phones in **a single window** — powered by our own mirroring engine, a scrcpy-server fork whose sources live in `engine/`.

Free, open source and community-driven: every line is readable, ideas are shared on Discord, and the app belongs to the people who use it.

## Features

| | |
|---|---|
| **HD mirroring** | Native phone resolution, 60/90/120 fps, H.264, H.265 and AV1 codecs |
| **Low latency** | Low-latency FFmpeg decoding, latest-frame priority, ~400 ms audio, optimized sockets |
| **On-screen keybinds** | Drop a marker on a spell, bind a key — 1 keypress = 1 tap at that spot. Adjustable style (pill, circle, minimal), opacity and size, saved per device. Fully manual: no repeat, no macros |
| **Virtual display** | The game runs on a dedicated virtual screen — the physical phone stays free. Landscape, portrait and **tablet** presets (apps switch to tablet UI) |
| **Multi-account** | Several phones in one window — one account per phone |
| **Workspaces** | "Solo", "Duo", "Stream" — devices, order, active mirror and per-device settings restored in one click; `Ctrl`+`Shift`+`1-9` to switch |
| **Instant detection** | Event-driven `adb track-devices` — the phone shows up as soon as it's plugged in, no polling |
| **Quality presets** | Performance / Balanced / Quality+ / Max — resolution, fps and bitrate applied in one click |
| **Resource saver** | Inactive mirrors stop decoding video (CPU/GPU saved) — recording keeps running in the background |
| **Built-in diagnostics** | USB verdicts (faulty cable, authorization, unstable link) and network diagnostics right in the app |
| **Automatic firewall** | Inbound rules checked and created on first launch — a single UAC prompt |
| **Customization** | Rename each phone (tiles + hub) and pick its accent color — right-click the device |
| **USB & WiFi** | Switch to wireless in one click, then unplug the cable — the stream keeps going |
| **Mouse = touch** | Click, drag, wheel = scroll, `Ctrl`+wheel = pinch-to-zoom (map zoom) |
| **Keyboard** | Typed text reaches the phone like a Bluetooth keyboard |
| **Clipboard** | Bidirectional — `Ctrl`+`V` pastes to the phone, copying on the phone reaches the PC |
| **MP4 recording** | Remux with no re-encode — files ready to play and upload |
| **PNG screenshots** | One click, saved to `Pictures\TouchMirror` |
| **Screen off** | The phone's physical display goes dark while mirroring — saves battery and AMOLED |
| **Built-in help** | Forum, encyclopedia and DofusDB in a browser panel without leaving the game |
| **Fullscreen** | `F11` or dedicated button, control bar appears at the top edge |
| **Capture mode** | Clean window for OBS — ideal for streaming |
| **Local API** | HTTP + SSE on localhost with a token — Stream Deck, OBS, scripts. No endpoint can inject input into the phone |
| **Plugins** | Embedded JavaScript engine (sandbox) — `plugin.json` manifest, official plugins verified by hash |
| **Updates** | The app detects new GitHub releases at startup |
| **iPhone / AirPlay** | *Coming soon* — iOS mirroring is being finalized |

## Comparison

Every tool has its strengths — here's where TouchMirror stands:

| Feature | TouchMirror | scrcpy | Vysor | Walky |
|---|:---:|:---:|:---:|:---:|
| Native Windows GUI | ✅ | — (CLI) | ✅ | ✅ |
| Multiple phones in one window | ✅ | — | — | ✅ |
| On-screen keybinds (key → tap) | ✅ | — | — | — |
| Virtual display / tablet mode | ✅ | ✅ (option) | — | ✅ |
| Multi-account on a single phone | *Soon* | — | — | ✅ |
| iPhone / iOS | *Soon* | — | ✅ | ✅ |
| Quality presets, built-in diagnostics | ✅ | — | — | ✅ |
| Built-in MP4 recording | ✅ | ✅ | — | — |
| Local API + sandboxed plugins | ✅ | — | — | — |
| Multi-language | *Soon* (EN README ready) | — | ✅ | — |
| Open source | ✅ MIT | ✅ Apache-2.0 | — | — |

*As of 09/16/2026 — feel free to open an issue if anything has changed.*

## Installation

### Ready-to-use build (recommended)

> **Note:** the app UI is currently in French — full multi-language support is on the roadmap. Everything below maps 1:1 to the French labels.

1. Download **`TouchMirror-win-x64.zip`** from the [latest release](https://github.com/shinzarou-eng/TouchMirror/releases/latest)
2. Unzip anywhere, run **`TouchMirror.exe`**
3. That's it — **adb is bundled**, the .NET runtime is included and FFmpeg extracts on first launch

### Phone setup

1. **Developer options → USB debugging** enabled
2. Plug in via USB, accept the authorization on the phone
3. Click **Connect** — the mirror shows up, you play from the PC

### WiFi mode

Menu **⋯ → Enable WiFi** while the cable is plugged in → the device switches to TCP/IP and reconnects automatically. Unplug the cable, the stream keeps going. *(PC and phone on the same network; must be redone after a phone reboot — Android limitation.)*

## On-screen keybinds

A keyboard key that taps a precise spot on screen — for spells, items, buttons:

1. Enable **⌨ Keybinds** in the toolbar
2. Click on a spell on screen → a marker appears
3. Press a key (`1`, `A`, `F1`…) → the marker takes the key's name
4. Exit edit mode → each keypress sends **one** tap at that spot

In edit mode: drag to move, right-click to delete, `Esc` to quit. Style, opacity and size are set in the panel at the top of the video. Positions are relative to the image — they survive window resizing, rotation and virtual display.

**Strictly manual:** 1 keypress = 1 tap, holding the key repeats nothing. It's an ergonomic shortcut, not automation.

## Virtual display

Settings → **VIDEO → Display**: instead of the physical screen, the mirror shows a dedicated Android virtual display (Android 10+):

- **The game runs in the virtual display** — you can use your phone normally at the same time
- **Landscape** presets (1080p/900p/720p), **portrait** (1080×1920) and **tablet** (1920×1200, 2560×1600)
- Tablet presets lower the density → apps switch to tablet UI (airier HUD)

## Multi-account

Allowed by Ankama: as many physical devices as you want, one account per phone.

1. Connect the first phone
2. Plug in the second → it shows up in the list → **Connect**
3. Click a thumbnail to target it — only the active tile receives actions and plays audio; inactive ones stop decoding to save CPU
4. From the keyboard: `Ctrl`+`Tab` to cycle, `Ctrl`+`1…9` to target directly

## Keyboard shortcuts

| Key | Action |
|---|---|
| `F11` | Fullscreen |
| `Ctrl` + `Tab` | Next / previous mirror (`+Shift`) |
| `Ctrl` + `1…9` | Activate mirror N directly |
| `Ctrl` + wheel | Pinch zoom |
| Bound key | Tap at the on-screen marker (keybinds) |
| Mouse on the video | Direct touch — no hidden shortcuts interfering with the game |

## Local API (optional)

Settings → **Local API**: exposes `http://127.0.0.1:<port>` protected by a token (`Authorization: Bearer <token>` or `?token=`).

| Endpoint | Effect |
|---|---|
| `GET /api/status` · `/api/mirrors` · `/api/devices` | App state, tiles and devices |
| `POST /api/mirrors/{n}/activate` | Switch mirror (= `Ctrl`+N) |
| `POST /api/mirrors/{n}/record` · `/screenshot` · `/disconnect` | Per-tile actions |
| `POST /api/devices/{serial}/connect` | Connect a device |
| `GET /api/events` | Real-time SSE stream (connections, active tile, REC…) |

**Compliance:** the API drives the app, never the game — no endpoint produces input on the phone.

### Plugins

**🧩 Plugins** panel in the side rail: a plugin = a `plugins/<name>/` folder with a `plugin.json` manifest (name, version, description) and a `plugin.js`. Code runs in an **embedded, sandboxed JavaScript engine** — no external process, no shell: the plugin only sees the `tm` object.

```text
plugins/
  reconnect/
    plugin.json    # metadata (name, version, author…)
    plugin.js      # logic, via the tm.* API
```

**Exposed API** (`tm.*`, control-plane only):

```javascript
await tm.getDevices();           // discovered devices
await tm.getMirrors();           // mirrors and their slots
await tm.connect("RFGL22M2JQM"); // connect a device
await tm.activate(0);            // slot 0 as the main mirror
await tm.screenshot(0);          // capture
tm.on("devices", e => …);        // real-time events
tm.setInterval(fn, ms); tm.setTimeout(fn, ms);
tm.log("message");               // → app log
```

No filesystem, network or process access from the sandbox — and like the local API, **nothing can inject input toward the phone**. The engine is bounded (memory, recursion, timers, 30 calls/s max) and every plugin action is logged.

A plugin is code: only install what you can read or what comes from us. Official plugins carry a green shield badge (SHA-256 verified hash) — any other plugin asks for confirmation before activation, and any change to an approved plugin requires your consent again. Plugins drive the app — never the game.

Included: **Reconnect** — restores a mirror whose session dropped (cable, WiFi, crash), never after a voluntary disconnect. The farm repairs itself without launching anything on the phone.

## Build from source

```bash
dotnet build src/AndroidMirror/TouchMirror.csproj
```

Requirements: **.NET 10 SDK** only — adb, the TouchMirror engine (`assets/touchmirror-engine.jar`) and the FFmpeg DLLs are bundled in the repo. Engine sources (scrcpy-server fork, Apache-2.0) live in `engine/` — rebuild via `engine/build-engine.ps1`.

## Tech stack

WPF / .NET 10 · WPF-UI · TouchMirror engine (scrcpy-server fork) · FFmpeg (decode + MP4 remux) · NAudio · WebView2

## Ankama compliance

TouchMirror displays and controls the **official game** running on your **real phone** — no emulator, no modified client, no macros or automation. Every action maps to a human gesture: on-screen keybinds send one tap per keypress, nothing more. This is the use case Ankama support confirmed as allowed (see the [official FAQ](https://support.ankama.com/hc/en-us/articles/26840828168209)).

**TouchMirror will never offer automation, bots or macros** — not today, not in a future version. The local API and plugins drive the application (mirroring, capture, recording, reconnect), never in-game actions: no API route injects touch, keyboard, text or clipboard toward Android. See the [roadmap out-of-scope section](ROADMAP.md).

## Roadmap

Next steps: finalizing iPhone mirroring (AirPlay + control), per-device audio output, and continued polish of the multi-phone experience.

**[Read the roadmap](ROADMAP.md)** — priorities, validation criteria and ideas under study. Planned items are not available features yet.

## Community

Questions, feedback, multi-account help → **[Discord](https://discord.gg/DBJ9kNCdX)** · Bugs and ideas → [GitHub issues](https://github.com/shinzarou-eng/TouchMirror/issues)

## License

[MIT](LICENSE) — free to use, modify and redistribute. The engine in `engine/` is Apache-2.0 (scrcpy-server fork).

---

<div align="center">
Made with ❤️ for the Dofus Touch community
</div>
