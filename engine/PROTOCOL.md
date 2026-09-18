# TouchMirror engine protocol

Wire format between the Windows client and `touchmirror-engine.jar` (version `4.1-tm.2`).

All integers are big-endian. The client is the only supported peer: the engine must be started with the client version as first argument (`app_process … com.touchmirror.engine.Server <version> [key=value …]`).

## Transport

- `adb reverse localabstract:touchmirror[_<scid_hex>] → tcp:<port>` — the client listens, the engine connects back.
- Up to 3 sockets are opened in this order: **video**, **audio** (skipped if `audio=false`), **control** (skipped if `control=false`).
- `send_dummy_byte` (default on): the engine writes one `0x00` byte on the video socket so the client can detect a broken connection.

## Startup arguments

`key=value` pairs parsed by `Options.java`. The client currently sends:
`scid`, `log_level`, `video`, `audio`, `audio_codec`, `video_codec`, `control`, `max_size`, `max_fps`,
`video_bit_rate`, `stay_awake`, `power_on`, `cleanup`, `new_display`, `start_app`, `display_id`.

Other parsed keys (accepted but unused by the client today): `min_size_alignment`, `angle`, `crop`,
`tunnel_forward`, `show_touches`, `screen_off_timeout`, `video_codec_options`, `audio_codec_options`,
`video_encoder`, `audio_encoder`, `power_off_on_close`, `clipboard_autosync`, `downsize_on_error`,
`vd_destroy_content`, `vd_system_decorations`, `flex_display`, `capture_orientation`,
`display_ime_policy`, `keep_active`, `ignore_video_encoder_constraints`,
`send_device_meta`, `send_frame_meta`, `send_dummy_byte`, `send_stream_meta`, `raw_stream`.

Unknown keys log a warning and are ignored.

## Video / audio streams

### Stream meta (if `send_stream_meta`, default on)

| Field | Size |
|---|---|
| codec id | 4 bytes |
| session flags | 4 bytes (bit 31 = session meta, bit 0 = client-side resize) |
| width | 4 bytes |
| height | 4 bytes |

The session block is only sent on the video stream.

Codec ids: h264 `0x68323634`, h265 `0x68323635`, av1 `0x00617631` (video);
opus `0x6f707573`, aac `0x00616163`, flac `0x666c6163`, raw `0x00726177` (audio).
`0x00000000`/`0x00000001` as codec id = stream disabled (continue / abort).

### Frame meta (if `send_frame_meta`, default on) — 12 bytes per packet

| Field | Size |
|---|---|
| pts + flags | 8 bytes — bit 63 = config packet, bit 62 = key frame |
| packet size | 4 bytes |
| payload | `packet size` bytes |

### Device meta (if `send_device_meta`, default on)

64 bytes on the video socket, before the stream meta: UTF-8 device name, zero-padded.

## Control channel (client → engine)

First byte = message type. Types not listed are rejected (`12`–`14` and `18`–`20` are retired).

| Type | Name | Payload |
|---|---|---|
| 0 | INJECT_KEYCODE | action u8, keycode i32, repeat u32, metastate u32 |
| 1 | INJECT_TEXT | length u32, utf8 |
| 2 | INJECT_TOUCH_EVENT | action u8, pointer_id i64, x i32, y i32, w u16, h u16, pressure u16 fixed, action_button u32, buttons u32 |
| 3 | INJECT_SCROLL_EVENT | x i32, y i32, w u16, h u16, hscroll i16 fixed, vscroll i16 fixed, buttons u32 |
| 4 | BACK_OR_SCREEN_ON | action u8 |
| 5 | EXPAND_NOTIFICATION_PANEL | — |
| 6 | EXPAND_SETTINGS_PANEL | — |
| 7 | COLLAPSE_PANELS | — |
| 8 | GET_CLIPBOARD | copy_key u8 |
| 9 | SET_CLIPBOARD | sequence i64, paste u8, length u32, utf8 |
| 10 | SET_DISPLAY_POWER | on u8 |
| 11 | ROTATE_DEVICE | — |
| 15 | OPEN_HARD_KEYBOARD_SETTINGS | — |
| 16 | START_APP | length u8, utf8 (`?name=…` or package) |
| 17 | RESET_VIDEO | — |
| 21 | RESIZE_DISPLAY | width u16, height u16 |
| 22 | SCAN_FILE | length u32, utf8 path |
| 23 | SET_VIDEO_PARAMS | bit_rate i32, suspend u8 |

## Control channel (engine → client)

| Type | Name | Payload |
|---|---|---|
| 0 | CLIPBOARD | length u32, utf8 |
| 1 | ACK_CLIPBOARD | sequence i64 |

## Changelog

- `4.1-tm.2` — fork protocol owned by TouchMirror: camera capture path, UHID virtual devices,
  mic/`audio_source` capture, catalog `list_*` options and the remote `video_source`/`audio_source`
  switches removed (message types 12–14 and 18–20 retired).
- `4.1-tm.1` — initial fork: `start_app` option, `SET_VIDEO_PARAMS` (23) for live bitrate/suspend.
