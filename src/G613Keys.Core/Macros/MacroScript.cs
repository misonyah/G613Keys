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

    private static KeyStroke? ParseKey(string spec)
    {
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var mods = Modifiers.None;
        ushort? vk = null;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Modifiers.Ctrl; break;
                case "shift": mods |= Modifiers.Shift; break;
                case "alt": mods |= Modifiers.Alt; break;
                case "win" or "cmd" or "meta": mods |= Modifiers.Win; break;
                default:
                    if (KeyNames.ToVk(p) is ushort v) vk = v;
                    break;
            }
        }
        return vk is ushort code ? new KeyStroke(code, mods) : null;
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
        _ => "",
    };

    private static string FormatKey(KeyStroke k)
    {
        var sb = new StringBuilder();
        if (k.Mods.HasFlag(Modifiers.Ctrl)) sb.Append("Ctrl+");
        if (k.Mods.HasFlag(Modifiers.Shift)) sb.Append("Shift+");
        if (k.Mods.HasFlag(Modifiers.Alt)) sb.Append("Alt+");
        if (k.Mods.HasFlag(Modifiers.Win)) sb.Append("Win+");
        sb.Append(KeyNames.FromVk(k.Vk));
        return sb.ToString();
    }
}
