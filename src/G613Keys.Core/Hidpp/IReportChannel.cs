namespace G613Keys.Core.Hidpp;

/// <summary>
/// A bidirectional HID report channel for one collection. Abstracted so the
/// transport's request/reply and notification routing can be unit-tested
/// without real hardware.
/// </summary>
public interface IReportChannel : IDisposable
{
    HidppReportKind Kind { get; }
    int OutputLength { get; }
    int InputLength { get; }

    /// <summary>Write a full output report (already sized to OutputLength).</summary>
    void Write(byte[] wire);

    /// <summary>
    /// Block for an inbound report up to <paramref name="timeoutMs"/>.
    /// Returns the (trimmed) report, or null on timeout.
    /// </summary>
    byte[]? Read(int timeoutMs);
}
