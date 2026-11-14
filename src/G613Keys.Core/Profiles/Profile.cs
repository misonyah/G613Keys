using G613Keys.Core.GKeys;
using G613Keys.Core.Macros;

namespace G613Keys.Core.Profiles;

/// <summary>
/// A per-application key map. <see cref="Match"/> is a foreground executable name
/// (case-insensitive, e.g. "notepad.exe"); "*" is the default profile used when
/// no app-specific profile matches.
/// </summary>
public sealed record Profile(string Match, Dictionary<GKey, List<MacroAction>> Keys)
{
    public const string DefaultMatch = "*";

    public bool IsDefault => Match == DefaultMatch;

    public static Profile EmptyDefault() => new(DefaultMatch, new Dictionary<GKey, List<MacroAction>>());
}
