namespace G613Keys.Core.Hidpp;

/// <summary>HID++ error codes relevant to device discovery. See HIDPP-FINDINGS §5.</summary>
public static class HidppError
{
    public const byte InvalidSubId = 0x01;  // 1.0; receiver answers 2.0 Root this way
    public const byte UnknownDevice = 0x08;  // empty slot
    public const byte ResourceError = 0x09;  // device present but asleep
}
