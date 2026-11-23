using G613Keys.Core.Hidpp;

namespace G613Keys.Core.GKeys;

public enum GKey { G1 = 0, G2 = 1, G3 = 2, G4 = 3, G5 = 4, G6 = 5 }

public readonly record struct GKeyEvent(GKey Key, bool Down);

/// <summary>
/// Enables GKeys divert on the device, decodes the press bitmap into down/up
/// events, and re-arms divert after sleep/reconnect. Protocol per
/// HIDPP-FINDINGS §7 (divert = feature 0x8010 func index 2, param0 1/0; events
/// carry a little-endian uint32 bitmap, bit0=G1 … bit5=G6, full state each time).
/// </summary>
public sealed class GKeyService : IDisposable
{
    public const byte DivertFuncIndex = 2;
    public const int GKeyCount = 6;

    private readonly HidppTransport _transport;
    private readonly HidppDevice _device;
    private readonly byte _featureIndex;
    private uint _lastBitmap;
    private bool _enabled;

    /// <summary>Raised for every key-down and key-up transition.</summary>
    public event Action<GKeyEvent>? KeyChanged;

    public GKeyService(HidppTransport transport, HidppDevice device, byte featureIndex)
    {
        if (featureIndex == 0)
            throw new ArgumentException("GKeys feature not supported (index 0)", nameof(featureIndex));
        _transport = transport;
        _device = device;
        _featureIndex = featureIndex;
        _transport.Notification += OnNotification;
    }

    public bool Enabled => _enabled;

    /// <summary>Turn divert on so the G-keys emit HID++ notifications.</summary>
    public bool Enable()
    {
        bool ok = SendDivert(true);
        if (ok) { _enabled = true; _lastBitmap = 0; }
        return ok;
    }

    /// <summary>Turn divert off (restores the keys' default behaviour).</summary>
    public bool Disable()
    {
        bool ok = SendDivert(false);
        _enabled = false;
        return ok;
    }

    /// <summary>
    /// Re-send divert after the keyboard woke or the receiver reconnected.
    /// Divert does not survive sleep, so the tray app calls this on device
    /// arrival and after read errors.
    /// </summary>
    public bool ReArm()
    {
        if (!_enabled) return false;
        _lastBitmap = 0;
        return SendDivert(true);
    }

    /// <summary>
    /// Periodic keep-alive: re-assert divert without touching the last-seen key
    /// state. The G613 silently clears divert when it idle-sleeps and re-wakes,
    /// reverting to default output; re-sending the enable (which the awake keyboard
    /// accepts) restores G-key events. Unlike <see cref="ReArm"/> this does not
    /// reset the bitmap, so a key held across a refresh is not re-fired as a press.
    /// </summary>
    public bool RefreshDivert() => _enabled && SendDivert(true);

    private bool SendDivert(bool on)
    {
        var req = HidppReport.Request(HidppReportKind.Long, _device.DeviceIndex, _featureIndex,
            DivertFuncIndex, (byte)(on ? 0x01 : 0x00));
        var reply = _transport.Request(req);
        return reply is not null && !reply.Is10Error && !reply.Is20Error;
    }

    private void OnNotification(HidppReport r)
    {
        // GKeys event: our feature index, function nibble 0, long report with bitmap.
        if (r.FeatureIndex != _featureIndex) return;
        if ((r.FuncSwId & 0x0F) != 0) return;
        if (r.Params.Length < 4) return;

        uint bitmap = (uint)(r.Params[0] | r.Params[1] << 8 | r.Params[2] << 16 | r.Params[3] << 24);
        foreach (var ev in Diff(_lastBitmap, bitmap))
            KeyChanged?.Invoke(ev);
        _lastBitmap = bitmap;
    }

    /// <summary>Emit a down/up event for every G-key bit that changed between two bitmaps.</summary>
    public static IEnumerable<GKeyEvent> Diff(uint prev, uint curr)
    {
        uint changed = prev ^ curr;
        for (int i = 0; i < GKeyCount; i++)
        {
            uint mask = 1u << i;
            if ((changed & mask) == 0) continue;
            yield return new GKeyEvent((GKey)i, (curr & mask) != 0);
        }
    }

    public void Dispose()
    {
        _transport.Notification -= OnNotification;
        if (_enabled)
        {
            try { SendDivert(false); } catch { /* best effort on shutdown */ }
        }
    }
}
