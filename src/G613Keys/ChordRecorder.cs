using G613Keys.Core.Input;
using G613Keys.Core.Macros;

namespace G613Keys;

/// <summary>
/// Captures real keypresses while active and turns each modifier+key chord into a
/// <c>key:</c> script line, for the config panel's "record" button. Swallows the
/// keys it records so they don't leak to whatever window is focused; Esc stops.
/// </summary>
internal sealed class ChordRecorder : IDisposable
{
    // Left/right/neutral virtual-key codes for each modifier.
    private const int VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    private const int VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const int VK_ESCAPE = 0x1B;

    private readonly KeyboardHook _hook = new();
    private Modifiers _held;
    private bool _active;

    /// <summary>Raised on the UI thread with each captured chord as a script line.</summary>
    public event Action<string>? ChordCaptured;

    /// <summary>Raised when the user presses Esc to finish recording.</summary>
    public event Action? Stopped;

    public ChordRecorder()
    {
        _hook.ShouldSwallow = _ => _active;      // while recording, nothing reaches other apps
        _hook.KeyEvent = OnKeyEvent;
    }

    public void Start()
    {
        _held = Modifiers.None;
        _active = true;
        _hook.Install();
    }

    public void Stop()
    {
        _active = false;
        _held = Modifiers.None;
    }

    private void OnKeyEvent(int vk, bool isDown)
    {
        if (!_active) return;

        if (TryModifier(vk, out var flag))
        {
            if (isDown) _held |= flag; else _held &= ~flag;
            return;
        }

        if (!isDown) return;                      // fire on key down only
        if (vk == VK_ESCAPE) { Stop(); Stopped?.Invoke(); return; }

        var line = MacroScript.FormatKeyChord((ushort)vk, _held);
        ChordCaptured?.Invoke(line);
    }

    private static bool TryModifier(int vk, out Modifiers flag)
    {
        flag = vk switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => Modifiers.Shift,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => Modifiers.Ctrl,
            VK_MENU or VK_LMENU or VK_RMENU => Modifiers.Alt,
            VK_LWIN or VK_RWIN => Modifiers.Win,
            _ => Modifiers.None,
        };
        return flag != Modifiers.None;
    }

    public void Dispose() => _hook.Dispose();
}
