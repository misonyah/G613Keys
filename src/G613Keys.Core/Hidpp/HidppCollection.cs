namespace G613Keys.Core.Hidpp;

/// <summary>
/// One of the receiver's vendor HID++ top-level collections (usage page 0xFF00).
/// Identified by HID usage, never by "largest report" — the 32-byte very-long /
/// DJ collection (usage 0x04) is a trap that rejects 0x11 writes. See
/// HIDPP-FINDINGS §2.
/// </summary>
public sealed record HidppCollection(
    string DevicePath,
    HidppReportKind Kind,
    int OutputReportByteLength,
    int InputReportByteLength)
{
    public byte ReportId => (byte)Kind;
}
