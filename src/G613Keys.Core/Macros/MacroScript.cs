using System.Globalization;
using System.Text;

namespace G613Keys.Core.Macros;

/// <summary>
/// A tiny human-editable macro syntax for the config UI. One action per line:
///   key: Ctrl+Shift+S
///   text: hello world
///   delay: 100
///   mouse: left
///   repeat 3 { key: Up }   (single-line repeat; body uses ';' between actions)
/// Round-trips via <see cref="Parse"/> / <see cref="Format"/>.
/// </summary>
public static class MacroScript
{
    public static IReadOnlyList<MacroAction> Parse(string text)
    {
        var actions = new List<MacroAction>();
        if (string.IsNullOrWhiteSpace(text)) return actions;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var action = ParseLine(line);
            if (action is not null) actions.Add(action);
        }
        return actions;
    }

    /// <summary>A problem with one line of a macro script, for inline UI feedback.</summary>
    public readonly record struct ScriptError(int Line, string Message);

    /// <summary>
    /// Check a script line by line without executing it. Returns one
    /// <see cref="ScriptError"/> per non-blank line that wouldn't parse, with a
    /// 1-based line number and a short reason. An empty list means the script is valid.
    /// </summary>
    public static IReadOnlyList<ScriptError> Validate(string text)
    {
        var errors = new List<ScriptError>();
        if (string.IsNullOrWhiteSpace(text)) return errors;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var reason = LineError(line);
            if (reason is not null) errors.Add(new ScriptError(i + 1, reason));
        }
        return errors;
    }

    /// <summary>Returns a human-readable reason the line is invalid, or null if it parses.</summary>
    private static string? LineError(string line)
    {
        if (line.StartsWith("repeat ", StringComparison.OrdinalIgnoreCase))
        {
            int brace = line.IndexOf('{');
            int end = line.LastIndexOf('}');
            if (brace < 0 || end < brace) return "repeat needs braces: repeat N { … }";
            if (!int.TryParse(line[7..brace].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return "repeat count must be a number";
            return ParseRepeat(line) is null ? "repeat body has no valid actions" : null;
        }

        int colon = line.IndexOf(':');
        if (colon < 0) return "expected 'kind: value' (e.g. key: Ctrl+S)";
        string kind = line[..colon].Trim().ToLowerInvariant();
        string arg = line[(colon + 1)..].Trim();
        return kind switch
        {
            "key" => KeyChordError(arg),
            "text" => null,
            "delay" => int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? null : "delay must be milliseconds (a number)",
            "mouse" => ParseMouse(arg) is null ? "mouse must be left, right or middle" : null,
            "run" => ParseRun(arg) is null ? "run needs a path" : null,
            _ => $"unknown action '{kind}' (use key, text, delay, mouse, run or repeat)",
        };
    }

    /// <summary>
    /// Validate a <c>key:</c> argument, flagging any token that is neither a known
    /// modifier nor a known key name. Unlike <see cref="ParseKey"/> (which tolerates a
    /// bare modifier chord like "Alt+Shift"), this rejects a typo'd key name such as
    /// "Ctrl+Nope" instead of silently degrading it to a lone modifier press.
    /// </summary>
    private static string? KeyChordError(string spec)
    {
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "key needs at least one key, e.g. key: Ctrl+S";
        bool anyModifier = false;
        int realKeys = 0;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control" or "shift" or "alt" or "win" or "cmd" or "meta":
                    anyModifier = true;
                    break;
                default:
                    if (KeyNames.ToVk(p) is null) return $"unknown key: '{p}'";
                    realKeys++;
                    break;
            }
        }
        return realKeys == 0 && !anyModifier ? "key needs at least one key" : null;
    }

    /// <summary>
    /// Build a <c>key:</c> script line from a captured modifier+key chord, for the
    /// config UI's record-a-keypress feature. Round-trips through <see cref="Parse"/>.
    /// </summary>
    public static string FormatKeyChord(ushort vk, Modifiers mods)
        => FormatLine(new KeyStroke(vk, mods));

    private static MacroAction? ParseLine(string line)
    {
        if (line.StartsWith("repeat ", StringComparison.OrdinalIgnoreCase))
            return ParseRepeat(line);

        int colon = line.IndexOf(':');
        if (colon < 0) return null;
        string kind = line[..colon].Trim().ToLowerInvariant();
        string arg = line[(colon + 1)..].Trim();

        return kind switch
        {
            "key" => ParseKey(arg),
            "text" => new TextInput(arg),
            "delay" => int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) ? new Delay(ms) : null,
            "mouse" => ParseMouse(arg),
            "run" => ParseRun(arg),
            _ => null,
        };
    }

    private static MacroAction? ParseRepeat(string line)
    {
        // repeat N { a ; b ; c }
        int brace = line.IndexOf('{');
        int end = line.LastIndexOf('}');
        if (brace < 0 || end < brace) return null;
        if (!int.TryParse(line[7..brace].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            return null;
        var body = new List<MacroAction>();
        foreach (var part in line[(brace + 1)..end].Split(';'))
        {
            var a = ParseLine(part.Trim());
            if (a is not null) body.Add(a);
        }
        return body.Count > 0 ? new Repeat(body, count) : null;
    }

    // Virtual-key codes for modifiers, for the modifier-only chord case.
    private const ushort VkCtrl = 0x11, VkShift = 0x10, VkAlt = 0x12, VkWin = 0x5B;

    private static KeyStroke? ParseKey(string spec)
    {
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var mods = Modifiers.None;
        ushort? vk = null;
        (Modifiers flag, ushort vk) lastMod = default;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Modifiers.Ctrl; lastMod = (Modifiers.Ctrl, VkCtrl); break;
                case "shift": mods |= Modifiers.Shift; lastMod = (Modifiers.Shift, VkShift); break;
                case "alt": mods |= Modifiers.Alt; lastMod = (Modifiers.Alt, VkAlt); break;
                case "win" or "cmd" or "meta": mods |= Modifiers.Win; lastMod = (Modifiers.Win, VkWin); break;
                default:
                    if (KeyNames.ToVk(p) is ushort v) vk = v;
                    break;
            }
        }
        if (vk is ushort code) return new KeyStroke(code, mods);
        // Modifier-only chord (e.g. "Alt+Shift"): use the last modifier as the key.
        if (lastMod.vk != 0) return new KeyStroke(lastMod.vk, mods & ~lastMod.flag);
        return null;
    }

    private static LaunchApp? ParseRun(string arg)
    {
        if (arg.Length == 0) return null;
        int sep = arg.IndexOf(" ::: ", StringComparison.Ordinal);
        return sep < 0
            ? new LaunchApp(arg.Trim())
            : new LaunchApp(arg[..sep].Trim(), arg[(sep + 5)..].Trim());
    }

    private static MouseAction? ParseMouse(string arg) => arg.ToLowerInvariant() switch
    {
        "left" => new MouseAction(MouseButton.Left),
        "right" => new MouseAction(MouseButton.Right),
        "middle" => new MouseAction(MouseButton.Middle),
        _ => null,
    };

    public static string Format(IReadOnlyList<MacroAction> actions)
        => string.Join("\n", actions.Select(FormatLine));

    private static string FormatLine(MacroAction a) => a switch
    {
        KeyStroke k => $"key: {FormatKey(k)}",
        TextInput t => $"text: {t.Text}",
        Delay d => $"delay: {d.Ms.ToString(CultureInfo.InvariantCulture)}",
        MouseAction m => $"mouse: {m.Button.ToString().ToLowerInvariant()}",
        Repeat r => $"repeat {r.Count.ToString(CultureInfo.InvariantCulture)} {{ {string.Join(" ; ", r.Body.Select(FormatLine))} }}",
        LaunchApp la => la.Arguments.Length > 0 ? $"run: {la.Path} ::: {la.Arguments}" : $"run: {la.Path}",
        _ => "",
    };

    private static string? ModVkName(ushort vk) => vk switch
    {
        VkCtrl => "Ctrl", VkShift => "Shift", VkAlt => "Alt", VkWin => "Win", _ => null,
    };

    private static string FormatKey(KeyStroke k)
    {
        var sb = new StringBuilder();
        if (k.Mods.HasFlag(Modifiers.Ctrl)) sb.Append("Ctrl+");
        if (k.Mods.HasFlag(Modifiers.Shift)) sb.Append("Shift+");
        if (k.Mods.HasFlag(Modifiers.Alt)) sb.Append("Alt+");
        if (k.Mods.HasFlag(Modifiers.Win)) sb.Append("Win+");
        sb.Append(ModVkName(k.Vk) ?? KeyNames.FromVk(k.Vk));
        return sb.ToString();
    }
}
