# TouchMirror engine protocol

Wire format between the Windows client and `touchmirror-engine.jar` — **TM/3** (engine version `3.0`).

All integers are big-endian. The client is the only supported peer: the engine must be started
with the client version as first argument (`app_process … com.touchmirror.engine.Server <version> <scid_hex>`).

## Transport

- `adb reverse localabstract:touchmirror[_<scid_hex>] → tcp:<port>` — the client listens, the engine connects back.
- **One** socket carries every channel, multiplexed by framed packets:

| Offset | Field | Size |
|---|---|---|
| 0 | channel | 1 |
| 1 | payload length | 4 |
| 5 | payload | `length` bytes |

Channels: `0` session (engine → client) · `1` video · `2` audio · `3` control (client → engine) ·
`4` device messages (engine → client). Unknown inbound channels are dropped.

## Session bootstrap

1. Engine connects, sends a channel-0 frame carrying the **hello**:

| Offset | Field | Size |
|---|---|---|
| 0 | magic `TMIR` | 4 |
| 4 | protocol version | 2 (= 3) |
| 6 | capabilities | 4 |
| 10 | device name length | 1 |
| 11 | device name (UTF-8) | ≤ 63 |

Capability bits: `0x01` video · `0x02` audio · `0x04` control · `0x08` clipboard ·
`0x10` h265 · `0x20` av1 · `0x40` virtual display.

2. The client answers with its **first channel-3 frame**: a `CONFIG` message (type `0x01`)
   carrying the session options as a TLV sequence `[field u8][length u8][value]`.
   The engine applies it, then starts capture. Fields:

| Id | Name | Value |
|---|---|---|
| 0x01 | audio | bool |
| 0x02 | video codec | u8 — 0 auto, 1 h264, 2 h265, 3 av1 |
| 0x03 | audio codec | u8 — 0 opus, 1 aac, 2 flac, 3 raw |
| 0x04 | max size | u16 |
| 0x05 | max fps | u8 |
| 0x06 | video bit rate | u32 |
| 0x07 | flags | u16 — bit0 video, bit1 stay_awake, bit2 power_on, bit3 cleanup, bit4 downsize_on_error, bit5 clipboard_autosync |
| 0x08 | new display | w u16, h u16, dpi u16 |
| 0x09 | start app | UTF-8 package |
| 0x0A | log level | u8 — 0 verbose … 4 error |

Unknown fields are skipped — older engines tolerate newer clients.

## Media channels (video = 1, audio = 2)

Each channel payload is one kind-tagged message:

| Kind | Name | Body |
|---|---|---|
| 0 | codec | codec id, 4 bytes |
| 1 | packet | pts i64 · flags u8 (bit0 config, bit1 key frame) · data |
| 2 | session meta | width u32 · height u32 · flags u8 (bit0 client-side resize) — video only |
| 3 | stream end | code u8 — 0 continue, 1 abort |

Codec ids: h264 `0x68323634`, h265 `0x68323635`, av1 `0x00617631` (video);
opus `0x6f707573`, aac `0x00616163`, flac `0x666c6163`, raw `0x00726177` (audio).

## Control channel (client → engine, channel 3)

One channel-3 frame payload = one control message. First byte = message type,
then the type-specific body. Unknown types are skipped at frame level.

| Type | Name | Body |
|---|---|---|
| 0x01 | CONFIG | TLV sequence (see bootstrap) |
| 0x10 | INJECT_KEYCODE | action u8, keycode i32, repeat u32, metastate u32 |
| 0x11 | INJECT_TEXT | length u32, utf8 |
| 0x12 | INJECT_TOUCH | action u8, pointer_id i64, x i32, y i32, w u16, h u16, pressure u16 fixed, action_button u32, buttons u32 |
| 0x13 | INJECT_SCROLL | x i32, y i32, w u16, h u16, hscroll i16 fixed, vscroll i16 fixed, buttons u32 |
| 0x20 | BACK_OR_SCREEN_ON | action u8 |
| 0x21 | EXPAND_NOTIFICATIONS | — |
| 0x22 | EXPAND_SETTINGS | — |
| 0x23 | COLLAPSE_PANELS | — |
| 0x24 | SET_DISPLAY_POWER | on u8 |
| 0x25 | ROTATE_DEVICE | — |
| 0x26 | HARD_KEYBOARD_SETTINGS | — |
| 0x27 | START_APP | length u8, utf8 (`?name=…` or package) |
| 0x28 | SCAN_FILE | length u32, utf8 path |
| 0x30 | RESET_VIDEO | — |
| 0x31 | RESIZE_DISPLAY | width u16, height u16 |
| 0x32 | SET_VIDEO_PARAMS | bit_rate i32, suspend u8 |
| 0x40 | GET_CLIPBOARD | copy_key u8 |
| 0x41 | SET_CLIPBOARD | sequence i64, paste u8, length u32, utf8 |

## Device messages (engine → client, channel 4)

One channel-4 frame payload = one device message.

| Type | Name | Body |
|---|---|---|
| 0x50 | CLIPBOARD | length u32, utf8 |
| 0x51 | ACK_CLIPBOARD | sequence i64 |
