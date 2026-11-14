# G613Keys

A small, self-contained Windows tray app that drives the **Logitech G613**
keyboard's six macro keys (**G1–G6**) **without Logitech G HUB** — so G HUB can be
uninstalled.

Everything else on the G613 (typing, media, volume, multi-device pairing) already
works through standard HID with no software. The one job G HUB still does for this
keyboard is the G-keys, and that is all G613Keys does: detect G1–G6 and run
configurable, per-application macros. No telemetry, no auto-updater, no
background service, no third-party integrations.

## How it works

The G-keys are read straight from the LIGHTSPEED receiver's raw **HID++ 2.0**
interface (the `GKeys` feature, `0x8010`). G613Keys enables "divert" so the keys
emit HID++ notifications instead of their default output, decodes the press
bitmap, and runs the macro bound to that key in the active profile. The full
protocol write-up is in [`docs/HIDPP-FINDINGS.md`](docs/HIDPP-FINDINGS.md).

The HID path is pure Win32 P/Invoke (`cfgmgr32` / `hid.dll` / `kernel32`) with
**no external dependencies**, so the core is obviously MIT-clean. The Linux tools
Solaar and logiops were read for the protocol only; no GPL code is used.

## Requirements

- Windows 10/11, x64
- .NET 10 Desktop Runtime (framework-dependent build)
- A Logitech G613 on its LIGHTSPEED receiver
- **G HUB must not be running** (it holds the HID++ interface exclusively). Quit
  it, or uninstall it once G613Keys works for you.

## Build

```
dotnet build -c Release
```

Run the tests:

```
dotnet test tests/G613Keys.Tests -c Release
```

Single-file publish:

```
dotnet publish src/G613Keys -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The result is a single `G613Keys.exe`.

## Use

Run `G613Keys.exe`. It lives in the system tray. Right-click for:

- **Enabled** — toggle G-key handling on/off
- **Configure…** — edit per-app profiles and the six keys (also on double-click)
- **Start with Windows** — opt-in per-user autostart (no service)
- **Quit**

The tray tooltip shows status (Running / keyboard asleep / no receiver / G-keys
unavailable).

### Profiles and macros

Profiles switch automatically with the foreground application (matched by
executable name, e.g. `notepad.exe`). The `*` profile is the default used when no
app-specific profile matches.

Each G-key holds a short macro script, one action per line:

```
key: Ctrl+Shift+S
text: hello world
delay: 100
mouse: left
repeat 3 { key: Up ; delay: 10 }
```

Modifiers are `Ctrl`, `Shift`, `Alt`, `Win`. Key names include letters/digits,
`F1`–`F24`, and names like `Enter`, `Tab`, `Esc`, `Space`, `Left`/`Up`/`Right`/
`Down`, `Home`, `End`, `PageUp`, `PageDown`, `Delete`, and the media/volume keys.

Profiles are stored as JSON at:

```
%LOCALAPPDATA%\G613Keys\profiles.json
```

## Status

The HID++ transport, device/feature discovery, divert, event decoding, macro
engine, and profile store are implemented and unit-tested. The exact GKeys event
layout and whether divert suppresses the keys' default output are confirmed on
first run against hardware — see the open items in
[`docs/HIDPP-FINDINGS.md`](docs/HIDPP-FINDINGS.md). If divert does not suppress
the default output, the built-in keyboard-hook fallback swallows it.

Only the G613 is targeted in v1; the layering doesn't preclude other Logitech
devices later.

## License

MIT — see [`LICENSE`](LICENSE).
