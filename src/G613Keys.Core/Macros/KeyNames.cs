namespace G613Keys.Core.Macros;

/// <summary>Maps friendly key names to Windows virtual-key codes and back.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, ushort> ToCode = Build();
    private static readonly Dictionary<ushort, string> FromCode = BuildReverse();

    public static ushort? ToVk(string name)
    {
        var n = name.Trim();
        if (n.Length == 1)
        {
            char c = char.ToUpperInvariant(n[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }
        return ToCode.TryGetValue(n.ToLowerInvariant(), out var vk) ? vk : null;
    }

    public static string FromVk(ushort vk)
    {
        if (vk is >= 'A' and <= 'Z') return ((char)vk).ToString();
        if (vk is >= '0' and <= '9') return ((char)vk).ToString();
        return FromCode.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}";
    }

    private static Dictionary<string, ushort> Build()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B,
            ["space"] = 0x20, ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D,
            ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22,
            ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
            ["printscreen"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14,
            ["numlock"] = 0x90, ["scrolllock"] = 0x91, ["apps"] = 0x5D, ["menu"] = 0x5D,
            ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["volumemute"] = 0xAD,
            ["medianext"] = 0xB0, ["mediaprev"] = 0xB1, ["mediastop"] = 0xB2, ["mediaplay"] = 0xB3,
        };
        for (ushort f = 1; f <= 24; f++)
            d[$"f{f}"] = (ushort)(0x70 + f - 1);
        return d;
    }

    private static Dictionary<ushort, string> BuildReverse()
    {
        // Prefer canonical display names.
        var d = new Dictionary<ushort, string>
        {
            [0x0D] = "Enter", [0x09] = "Tab", [0x1B] = "Esc", [0x20] = "Space",
            [0x08] = "Backspace", [0x2E] = "Delete", [0x2D] = "Insert",
            [0x24] = "Home", [0x23] = "End", [0x21] = "PageUp", [0x22] = "PageDown",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "PrintScreen", [0x13] = "Pause", [0x14] = "CapsLock",
            [0x90] = "NumLock", [0x91] = "ScrollLock", [0x5D] = "Apps",
            [0xAF] = "VolumeUp", [0xAE] = "VolumeDown", [0xAD] = "VolumeMute",
            [0xB0] = "MediaNext", [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlay",
        };
        for (ushort f = 1; f <= 24; f++)
            d[(ushort)(0x70 + f - 1)] = $"F{f}";
        return d;
    }
}
