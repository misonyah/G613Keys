using System.Text.Json.Serialization;

namespace G613Keys.Core.Macros;

[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

public enum MouseButton { Left, Right, Middle }

/// <summary>
/// One step of a macro. Executed in order by <see cref="MacroEngine"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyStroke), "key")]
[JsonDerivedType(typeof(TextInput), "text")]
[JsonDerivedType(typeof(Delay), "delay")]
[JsonDerivedType(typeof(MouseAction), "mouse")]
[JsonDerivedType(typeof(Repeat), "repeat")]
[JsonDerivedType(typeof(LaunchApp), "run")]
public abstract record MacroAction;

/// <summary>Press a virtual-key with optional modifiers (held for the stroke).</summary>
public sealed record KeyStroke(ushort Vk, Modifiers Mods = Modifiers.None) : MacroAction;

/// <summary>Type literal Unicode text.</summary>
public sealed record TextInput(string Text) : MacroAction;

/// <summary>Wait a number of milliseconds.</summary>
public sealed record Delay(int Ms) : MacroAction;

/// <summary>Click a mouse button (optionally after moving to absolute X,Y in 0..65535 space).</summary>
public sealed record MouseAction(MouseButton Button, int? X = null, int? Y = null) : MacroAction;

/// <summary>Repeat a body of actions a fixed number of times.</summary>
public sealed record Repeat(IReadOnlyList<MacroAction> Body, int Count) : MacroAction;

/// <summary>Launch an executable (optionally with arguments), like G HUB's "launch application".</summary>
public sealed record LaunchApp(string Path, string Arguments = "") : MacroAction;
