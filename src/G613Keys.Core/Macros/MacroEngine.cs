using G613Keys.Core.Native;
using static G613Keys.Core.Native.Win32Input;

namespace G613Keys.Core.Macros;

/// <summary>
/// Executes a macro action sequence via SendInput. The send and delay functions
/// are injectable so execution is fully unit-testable without real input.
/// </summary>
public sealed class MacroEngine
{
    // Virtual-key codes for the modifier keys.
    private const ushort VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_LWIN = 0x5B;

    private readonly Func<INPUT[], uint> _send;
    private readonly Func<int, CancellationToken, Task> _delay;

    public MacroEngine(Func<INPUT[], uint>? send = null, Func<int, CancellationToken, Task>? delay = null)
    {
        _send = send ?? (inputs => SendInput((uint)inputs.Length, inputs, InputSize));
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    public async Task Run(IReadOnlyList<MacroAction> actions, CancellationToken ct = default)
    {
        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            switch (action)
            {
                case KeyStroke k:
                    Emit(BuildKeyStroke(k));
                    break;
                case TextInput t:
                    Emit(BuildText(t.Text));
                    break;
                case Delay d:
                    await _delay(d.Ms, ct).ConfigureAwait(false);
                    break;
                case MouseAction m:
                    Emit(BuildMouse(m));
                    break;
                case Repeat r:
                    for (int i = 0; i < r.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Run(r.Body, ct).ConfigureAwait(false);
                    }
                    break;
            }
        }
    }

    private void Emit(INPUT[] inputs)
    {
        if (inputs.Length > 0) _send(inputs);
    }

    private static IEnumerable<ushort> ModifierVks(Modifiers mods)
    {
        if (mods.HasFlag(Modifiers.Ctrl)) yield return VK_CONTROL;
        if (mods.HasFlag(Modifiers.Shift)) yield return VK_SHIFT;
        if (mods.HasFlag(Modifiers.Alt)) yield return VK_MENU;
        if (mods.HasFlag(Modifiers.Win)) yield return VK_LWIN;
    }

    private static INPUT[] BuildKeyStroke(KeyStroke k)
    {
        var mods = ModifierVks(k.Mods).ToArray();
        var list = new List<INPUT>(mods.Length * 2 + 2);
        foreach (var vk in mods) list.Add(KeyDown(vk));
        list.Add(KeyDown(k.Vk));
        list.Add(KeyUp(k.Vk));
        for (int i = mods.Length - 1; i >= 0; i--) list.Add(KeyUp(mods[i]));
        return list.ToArray();
    }

    private static INPUT[] BuildText(string text)
    {
        var list = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            list.Add(UnicodeDown(c));
            list.Add(UnicodeUp(c));
        }
        return list.ToArray();
    }

    private static INPUT[] BuildMouse(MouseAction m)
    {
        var list = new List<INPUT>(3);
        if (m.X is int x && m.Y is int y)
            list.Add(Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, x, y));
        var (down, up) = m.Button switch
        {
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };
        list.Add(Mouse(down));
        list.Add(Mouse(up));
        return list.ToArray();
    }

    private static INPUT KeyDown(ushort vk) => Key(vk, 0, 0);
    private static INPUT KeyUp(ushort vk) => Key(vk, 0, KEYEVENTF_KEYUP);
    private static INPUT UnicodeDown(char c) => Key(0, c, KEYEVENTF_UNICODE);
    private static INPUT UnicodeUp(char c) => Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static INPUT Mouse(uint flags, int x = 0, int y = 0) => new()
    {
        type = INPUT_MOUSE,
        u = new INPUTUNION { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags } },
    };
}
