# G613Keys — design

## 1. Intent & why

Replace Logitech G HUB entirely for a **Logitech G613** keyboard so it can be
uninstalled. G HUB runs an updater service, a LampArray service, a tray agent and
several integration applets — a lot of resident software whose only remaining job
for this keyboard is driving the six macro keys **G1–G6**. Everything else on the
G613 (typing, media, volume, multi-device pairing) already works through standard
HID with no software, and the keyboard has no RGB. The goal is a small,
self-contained, **MIT-licensed** Windows tray app that detects G1–G6 and runs
configurable, per-application macros — nothing else G HUB does is wanted.

## 2. Goals

- Tray-only Windows app that, **with G HUB not running**, detects G1–G6 presses on
  the G613 and runs configurable actions.
- **Per-application profiles**: the active macro set switches with the foreground
  app (by process/exe name).
- **Macro engine**: keystroke sequences with modifiers, literal text, inter-key
  delays; plus mouse actions and repeat. Output via `SendInput`.
- **Config UI** to define/edit per-app profiles and the six keys' actions.
- JSON profile storage under `%LOCALAPPDATA%`.
- No telemetry, no auto-updater, no third-party integrations.

## 3. Non-goals

- No RGB / lighting (the G613 has none).
- No remapping of the other keys, media keys, volume, or pairing (already work
  without software).
- No support for other Logitech devices in v1 (design cleanly so it's not
  precluded, but only the G613 is targeted).
- No onboard-memory programming / firmware features.

## 4. Anti-goals (would make it a failure even if it "works")

- **Double input**: a G-key firing both its default output *and* the macro.
- Requiring any Logitech software, service, or driver to be installed/running.
- A background service, elevated privileges, or anything that phones home.
- Being brittle to the keyboard's aggressive sleep / receiver reconnects (macros
  must keep working after the keyboard wakes).

## 5. Constraints

- **Stack:** C#/.NET 10, WinForms, single tray app.
- **MIT licensing:** the only prior art for G-keys is GPL and Linux-only (Solaar,
  logiops). Those were **read** for the HID++ protocol; this is a clean
  implementation. No GPL code copied.
- **No external NuGet for the core HID path** — direct Win32 P/Invoke
  (`cfgmgr32`/`hid.dll`/`kernel32`), so the core has zero dependency risk.
- Device discovery must be **robust**, not hardcoded PIDs/slots (receiver vs.
  direct vs. Bluetooth; device sleeps).

## 6. Approaches considered

**A. HID++ GKeys divert via the receiver's raw vendor interface (CHOSEN).** Open
the receiver's HID++ short/long collections directly, discover the device slot and
the GKeys (0x8010) feature, enable divert so G-keys emit HID++ notifications, read
the press bitmap, and run macros. *Trade-off:* carries protocol risk (validated on
hardware) and must re-arm divert after sleep/reconnect. *Why chosen:* it is the
only approach that fully removes G HUB while reading the real G-keys.

**B. Low-level keyboard hook on the default output.** If the G-keys emit a fixed
default layout (e.g. F1–F6), a `WH_KEYBOARD_LL` hook could catch and
swallow+replace them. *Trade-off:* only works if the keys have a usable,
collision-free default (F1–F6 clashes with real F-keys). Fragile. *Rejected as the
primary mechanism*, but reused in Approach A as the **suppression fallback** if
divert turns out not to silence the default output.

**C. Keep a stripped-down lghub_agent just for G-keys.** *Rejected:* defeats the
purpose (can't uninstall G HUB) and isn't MIT. Kept only as the documented
fallback to report if Approach A proves infeasible.

## 7. Decided design (Approach A)

Layers, each independently testable:

1. **HID transport** (`Hidpp` core): enumerate via
   `CM_Get_Device_Interface_List`, select the HID++ short/long collections (usage
   page 0xFF00, usage 0x01/0x02), overlapped `CreateFile`/read/write,
   request/reply matching by feature index + swId, a notification callback for
   unmatched reports. Framing per `HIDPP-FINDINGS.md` §4. A testable
   `IReportChannel` seam lets the routing be unit-tested without hardware.
2. **Device/feature layer**: discover the G613's device index by the
   `getFeature`-error probe (findings §5), resolve the 0x8010 feature index,
   enable divert, raise `GKeyChanged(down/up)` decoded from the bitmap, and
   re-arm divert on device arrival.
3. **Macro engine**: an action model — `KeyStroke`, `TextInput`, `Delay`,
   `MouseAction`, `Repeat` — executed via `SendInput`, with the send and delay
   functions injected so execution is unit-testable.
4. **Profile store**: `profiles.json` under `%LOCALAPPDATA%\G613Keys\`, mapping
   foreground-exe → {G1..G6 → action sequence}, plus a default profile.
   Hot-reload on change; safe defaults on a missing/invalid file.
5. **Foreground watcher**: `GetForegroundWindow` → PID → process name to pick the
   active profile.
6. **Tray + config UI** (WinForms): tray icon, enable/disable, config window,
   opt-in per-user autostart, no service. Device-arrival awareness via
   `RegisterDeviceNotification` / `WM_DEVICECHANGE` on a hidden window.

## 8. Feasibility gate

Approach A was validated on hardware except the awake-keyboard capture: the
receiver answers HID++ with G HUB stopped, the G613 sits at a discoverable device
index, and the transport read/write/encode path works. The remaining items (exact
GKeys event index/layout, whether divert suppresses the default output, re-arm
after sleep, Bluetooth path) are resolved by the first run against hardware and
recorded in `HIDPP-FINDINGS.md`. The feature index is resolved at runtime, and the
keyboard-hook fallback covers the suppression case if divert is insufficient.
