namespace G613Keys.Core.Hidpp;

/// <summary>
/// Device addressing and feature discovery over a transport. Nothing is
/// hardcoded: the device slot is found by the Root.getFeature error-code probe
/// and feature indices are resolved at runtime (HIDPP-FINDINGS §5–§6).
/// </summary>
public sealed class HidppDevice
{
    public const ushort GKeysFeatureId = 0x8010;

    private readonly HidppTransport _transport;

    public byte DeviceIndex { get; }

    public HidppDevice(HidppTransport transport, byte deviceIndex)
    {
        _transport = transport;
        DeviceIndex = deviceIndex;
    }

    /// <summary>
    /// Probe device indices 1..6 for the keyboard. A slot that gives a normal
    /// reply or a RESOURCE_ERROR (present but asleep) is the device; an
    /// UNKNOWN_DEVICE slot is empty. Returns null if nothing answers.
    /// </summary>
    public static byte? FindDeviceIndex(HidppTransport transport, int perProbeTimeoutMs = 400)
    {
        byte? asleep = null;
        for (byte s = 1; s <= 6; s++)
        {
            var reply = ProbeRoot(transport, s, perProbeTimeoutMs);
            if (reply is null) continue;

            if (!reply.Is10Error && !reply.Is20Error)
                return s; // awake, answered the feature query

            if (reply.ErrorCode == HidppError.ResourceError)
                asleep ??= s; // present but asleep — remember, keep scanning for an awake one
        }
        return asleep;
    }

    private static HidppReport? ProbeRoot(HidppTransport transport, byte deviceIndex, int timeoutMs)
    {
        // Root (feature index 0), getFeature (func 0), args = feature id 0x8010 big-endian.
        var req = HidppReport.Request(HidppReportKind.Short, deviceIndex, 0x00, 0x00, 0x80, 0x10);
        return transport.Request(req, timeoutMs);
    }

    /// <summary>
    /// Resolve a feature's index via Root.getFeature. Returns 0 when the feature
    /// is not supported by the device.
    /// </summary>
    public byte GetFeatureIndex(ushort featureId, int timeoutMs = 500)
    {
        byte hi = (byte)(featureId >> 8), lo = (byte)(featureId & 0xFF);
        var req = HidppReport.Request(HidppReportKind.Short, DeviceIndex, 0x00, 0x00, hi, lo);
        var reply = _transport.Request(req, timeoutMs);
        if (reply is null || reply.Is10Error || reply.Is20Error) return 0;
        return reply.Params.Length >= 1 ? reply.Params[0] : (byte)0;
    }

    /// <summary>True if the device exposes the GKeys (0x8010) feature.</summary>
    public bool GKeysSupported => GetFeatureIndex(GKeysFeatureId) != 0;
}
