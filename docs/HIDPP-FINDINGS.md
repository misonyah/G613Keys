# G613 G-key access over HID++ 2.0 — findings

Working record of how the Logitech G613's G1–G6 macro keys can be read on
Windows **without** Logitech G HUB / the Logitech driver, via the LIGHTSPEED
receiver's raw HID++ interface.

Status legend: **[confirmed]** proven on hardware · **[from source]** read from
Solaar/logiops · **[pending]** needs the first-run hardware check (the app
resolves these at runtime; this note is updated once observed).

## 1. Target condition is real [confirmed]

With **G HUB fully stopped** (updater + lamparray services disabled, agent not
running), the receiver's HID++ interface is still fully present and answers
requests. So a replacement tool needs no Logitech software resident.

## 2. Device topology [confirmed]

- Keyboard: **Logitech G613**, connected through a **LIGHTSPEED receiver**
  (`USB\VID_046D&PID_C53D`). The same keyboard can also pair over Bluetooth and
  additionally enumerates a plain `HID Keyboard Device` (`PID_C232`); normal
  typing/media/volume already work with no software through those standard
  collections. Only G1–G6 need custom handling.
- The receiver exposes three vendor HID++ top-level collections on interface
  **MI_02**, distinguished by HID usage (all usage page `0xFF00`):

  | Collection | Usage | Report len | Report ID | Role |
  |-----------|-------|-----------|-----------|------|
  | MI_02 Col01 | 0x01 | 7  | **0x10** | HID++ **short** |
  | MI_02 Col02 | 0x02 | 20 | **0x11** | HID++ **long** |
  | MI_02 Col03 | 0x04 | 32 | 0x12 | HID++ very-long / DJ (unused) |

  Pick the collection by `UsagePage==0xFF00 && Usage==0x01/0x02`, **not** by
  "largest report" — Col03 (32-byte) is a trap that rejects `0x11` writes with
  `ERROR_INVALID_PARAMETER`.

## 3. Opening and I/O on Windows [confirmed]

- Enumerate via `CM_Get_Device_Interface_List(GUID_DEVINTERFACE_HID)`, filter
  paths containing `vid_046d` + `pid_c53d`, read caps with
  `HidD_GetPreparsedData` + `HidP_GetCaps`.
- Open with `CreateFile(... GENERIC_READ|GENERIC_WRITE, FILE_SHARE_RW,
  OPEN_EXISTING, FILE_FLAG_OVERLAPPED)`.
- **Gotcha:** because the handle is overlapped, both `WriteFile` and `ReadFile`
  **must** pass an `OVERLAPPED`; passing `NULL` returns err 87
  (`ERROR_INVALID_PARAMETER`). Use event-backed overlapped I/O with
  `WaitForSingleObject` + `GetOverlappedResult`, and `CancelIo` on timeout.
- Short HID++ requests get their replies on the **short** collection's input.
  Keep one dedicated reader per open collection and route replies to the caller;
  never issue a competing `ReadFile` from the request path (notifications
  arriving mid-request would otherwise be lost).

## 4. HID++ framing [confirmed]

Wire layout (byte 0 is the report ID):

```
[ reportId, deviceIndex, featureIndex, (funcIndexHi | swId), param0, param1, ... ]
```

- `reportId` = 0x10 (short, 7 bytes) or 0x11 (long, 20 bytes).
- `funcIndexHi` is the function index shifted into the high nibble (func 0 →
  0x00, func 1 → 0x10, func 2 → 0x20 …); the low nibble is the software id.
  This implementation uses **swId = 0x08** (Solaar uses 0x0B — any non-zero
  nibble works, it just tags our replies).
- A normal reply echoes `featureIndex` and `(funcHi|swId)`.
- **HID++ 2.0 error**: `featureIndex == 0xFF`, then echoed feature, echoed
  `func|swId`, error code.
- **HID++ 1.0 error**: `subId(byte2) == 0x8F`, then echoed subId, echoed
  address, error code. (The receiver answers unknown 2.0 requests to the
  receiver address this way.)

## 5. Device addressing [confirmed]

Probe `Root.getFeature` across device indices and read the error code:

- `0xFF` → the **receiver** itself (answered 2.0 Root with 1.0 `INVALID_SUBID`
  0x01).
- `0x01` → the **G613**. When the keyboard is asleep this returns 1.0
  `RESOURCE_ERROR` (0x09); empty slots `0x02..0x06` return `UNKNOWN_DEVICE`
  (0x08). The differing error is how the slot is identified without hardcoding.
- Don't hardcode the slot; discover it by this probe.

## 6. Feature discovery [confirmed]

`Root` is feature index 0, function 0 (`getFeature`). Request to device D:

```
10 D 00 08 80 10      (reportId, devIndex, Root=0, func0|swid8, 0x8010 big-endian)
```

Reply: `10 D 00 08 <gkeysFeatureIndex> <type> <version>`. Feature index 0 means
unsupported.

## 7. GKeys 0x8010 — enable + events

- Feature id **0x8010** (`GKEY`). logiops does **not** implement G-keys (it uses
  ReprogControls 0x1B04 for mice); **Solaar** is the reference. [from source]
- **Enable divert** = function index **2** (`funcHi 0x20`), `param[0]=0x01` on,
  `0x00` off. Solaar cannot read the state back. [from source]
- **G-key event** [from source, **pending** hardware confirm]: a long report,
  `featureIndex` byte = the GKeys index, function nibble 0, then a
  **little-endian uint32 bitmap** at bytes 4–7, `bit0=G1 … bit5=G6`. Each event
  carries the **full current state**; derive key-down/up by diffing against the
  previous bitmap.

## 8. Open items — resolved by the first-run hardware check

The feature-enable and read code is written and resolves the feature index at
runtime; the items below are confirmed by running the app once with the keyboard
awake (it sleeps aggressively):

1. The actual GKeys feature index value on the G613. **[pending]**
2. The exact event report (which collection it arrives on; bitmap layout).
   **[pending]**
3. **Suppression:** whether enabling divert stops the keys' default output
   (reported to default to F1–F6), i.e. no double-input. If not, the app swallows
   the default via its low-level keyboard hook (`KeyboardHook`). **[pending]**
4. **Persistence:** divert almost certainly does **not** survive sleep / receiver
   reconnect; the app re-enables it on `WM_DEVICECHANGE` arrival.
   **[pending — treated as required]**
5. **Bluetooth:** if the G613 is used on Bluetooth instead of the receiver, a
   separate HID++-over-BT path is needed; not yet explored. **[pending]**
