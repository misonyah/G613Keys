namespace G613Keys.Core.Hidpp;

/// <summary>HID++ report class, keyed by report id (byte 0 on the wire).</summary>
public enum HidppReportKind : byte
{
    Short = 0x10, // 7 bytes
    Long = 0x11,  // 20 bytes
}

/// <summary>
/// A HID++ 2.0 frame. Wire layout (byte 0 is the report id):
/// [ reportId, deviceIndex, featureIndex, (funcIndexHi | swId), param0, param1, ... ]
/// See docs/HIDPP-FINDINGS.md §4.
/// </summary>
public sealed record HidppReport(
    byte ReportId,
    byte DeviceIndex,
    byte FeatureIndex,
    byte FuncSwId,
    byte[] Params)
{
    /// <summary>Software id we tag our requests with (any non-zero nibble works).</summary>
    public const byte SwId = 0x08;

    public const int ShortLength = 7;
    public const int LongLength = 20;

    /// <summary>Function index (the high nibble of FuncSwId).</summary>
    public byte FuncIndex => (byte)(FuncSwId >> 4);

    /// <summary>Software id (the low nibble of FuncSwId).</summary>
    public byte SoftwareId => (byte)(FuncSwId & 0x0F);

    public static int LengthFor(HidppReportKind kind) =>
        kind == HidppReportKind.Short ? ShortLength : LongLength;

    /// <summary>
    /// Build a request. <paramref name="funcIndex"/> is the plain function index
    /// (0,1,2…); it is shifted into the high nibble and OR'd with SwId.
    /// </summary>
    public static HidppReport Request(
        HidppReportKind kind,
        byte deviceIndex,
        byte featureIndex,
        byte funcIndex,
        params byte[] parms)
    {
        return new HidppReport(
            (byte)kind,
            deviceIndex,
            featureIndex,
            (byte)((funcIndex << 4) | SwId),
            parms ?? []);
    }

    /// <summary>Serialize to a wire buffer of the given length (zero-padded).</summary>
    public byte[] ToWire(int length)
    {
        var buf = new byte[length];
        buf[0] = ReportId;
        buf[1] = DeviceIndex;
        buf[2] = FeatureIndex;
        buf[3] = FuncSwId;
        int n = Math.Min(Params.Length, length - 4);
        for (int i = 0; i < n; i++)
            buf[4 + i] = Params[i];
        return buf;
    }

    public byte[] ToWire() => ToWire(LengthFor((HidppReportKind)ReportId));

    /// <summary>Parse a received wire buffer. Params are the bytes after index 3.</summary>
    public static HidppReport Parse(byte[] wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (wire.Length < 4)
            throw new ArgumentException("HID++ report too short", nameof(wire));
        var parms = new byte[wire.Length - 4];
        Array.Copy(wire, 4, parms, 0, parms.Length);
        return new HidppReport(wire[0], wire[1], wire[2], wire[3], parms);
    }

    /// <summary>HID++ 2.0 error: featureIndex byte == 0xFF.</summary>
    public bool Is20Error => FeatureIndex == 0xFF;

    /// <summary>HID++ 1.0 error: byte 2 (featureIndex position) == 0x8F.</summary>
    public bool Is10Error => FeatureIndex == 0x8F;

    /// <summary>
    /// Error code for a 1.0 or 2.0 error reply, or null if this is not an error.
    /// On the wire both are [b2, b3, b4, code] starting at byte 2, so the code
    /// lands at Params[1] (Params = wire[4..]).
    /// 2.0: [FF, feat, func|swId, code]. 1.0: [8F, subId, address, code].
    /// </summary>
    public byte? ErrorCode =>
        (Is20Error || Is10Error) && Params.Length >= 2 ? Params[1] : null;
}
