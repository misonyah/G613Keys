using G613Keys.Core.Macros;
using Xunit;

namespace G613Keys.Tests;

public class MacroScriptTests
{
    [Fact]
    public void Parse_KeyWithModifiers()
    {
        var a = Assert.IsType<KeyStroke>(Assert.Single(MacroScript.Parse("key: Ctrl+Shift+S")));
        Assert.Equal((ushort)'S', a.Vk);
        Assert.Equal(Modifiers.Ctrl | Modifiers.Shift, a.Mods);
    }

    [Fact]
    public void Parse_TextDelayMouse()
    {
        var actions = MacroScript.Parse("text: hello world\ndelay: 120\nmouse: right");
        Assert.Equal("hello world", Assert.IsType<TextInput>(actions[0]).Text);
        Assert.Equal(120, Assert.IsType<Delay>(actions[1]).Ms);
        Assert.Equal(MouseButton.Right, Assert.IsType<MouseAction>(actions[2]).Button);
    }

    [Fact]
    public void Parse_FunctionKeyAndSpecial()
    {
        Assert.Equal((ushort)0x74, Assert.IsType<KeyStroke>(Assert.Single(MacroScript.Parse("key: F5"))).Vk);
        Assert.Equal((ushort)0x0D, Assert.IsType<KeyStroke>(Assert.Single(MacroScript.Parse("key: Enter"))).Vk);
    }

    [Fact]
    public void Parse_Repeat()
    {
        var r = Assert.IsType<Repeat>(Assert.Single(MacroScript.Parse("repeat 3 { key: Up ; delay: 10 }")));
        Assert.Equal(3, r.Count);
        Assert.Equal(2, r.Body.Count);
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var text = "key: Ctrl+C\ntext: abc\ndelay: 50\nkey: F12";
        var again = MacroScript.Format(MacroScript.Parse(text));
        Assert.Equal(text, again);
    }

    [Fact]
    public void Parse_Run_WithArgs()
    {
        var la = Assert.IsType<LaunchApp>(Assert.Single(
            MacroScript.Parse(@"run: C:\Program Files\App\x.exe ::: -backlight 100")));
        Assert.Equal(@"C:\Program Files\App\x.exe", la.Path);
        Assert.Equal("-backlight 100", la.Arguments);
    }

    [Fact]
    public void Parse_ModifierOnlyChord()
    {
        var k = Assert.IsType<KeyStroke>(Assert.Single(MacroScript.Parse("key: Alt+Shift")));
        Assert.Equal((ushort)0x10, k.Vk);          // Shift as the key
        Assert.Equal(Modifiers.Alt, k.Mods);        // Alt as the modifier
    }

    [Fact]
    public void Format_RoundTrips_RunAndChord()
    {
        var text = "key: Alt+Shift\nrun: C:\\a\\b.exe ::: -x 1\nkey: Alt+`";
        Assert.Equal(text, MacroScript.Format(MacroScript.Parse(text)));
    }

    [Fact]
    public void Parse_IgnoresBlankAndUnknownLines()
    {
        var actions = MacroScript.Parse("\n  \nbogus line\nkey: A\n");
        var only = Assert.Single(actions);
        Assert.Equal((ushort)'A', Assert.IsType<KeyStroke>(only).Vk);
    }

    [Fact]
    public void Validate_CleanScript_HasNoErrors()
    {
        var errors = MacroScript.Validate("key: Ctrl+C\ntext: hi\ndelay: 50\nmouse: left\nrepeat 2 { key: Up }\nrun: a.exe");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_BlankLines_AreSkipped()
    {
        Assert.Empty(MacroScript.Validate("\n   \n\tkey: A\n\n"));
    }

    [Fact]
    public void Validate_ReportsLineNumberAndReason()
    {
        // line 1 ok, line 2 unknown kind, line 3 bad delay, line 4 unknown key name
        var errors = MacroScript.Validate("key: A\nwat: x\ndelay: fast\nkey: Ctrl+Nope");
        Assert.Equal(3, errors.Count);
        Assert.Equal(2, errors[0].Line);
        Assert.Equal(3, errors[1].Line);
        Assert.Equal(4, errors[2].Line);
        Assert.Contains("delay", errors[1].Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoColon_IsAnError()
    {
        var e = Assert.Single(MacroScript.Validate("just some words"));
        Assert.Equal(1, e.Line);
    }

    [Fact]
    public void Validate_MalformedRepeat_IsAnError()
    {
        Assert.NotEmpty(MacroScript.Validate("repeat 3 key: Up"));   // missing braces
        Assert.NotEmpty(MacroScript.Validate("repeat x { key: Up }")); // bad count
    }

    [Fact]
    public void FormatKeyChord_BuildsKeyLine()
    {
        Assert.Equal("key: Ctrl+Shift+S",
            MacroScript.FormatKeyChord((ushort)'S', Modifiers.Ctrl | Modifiers.Shift));
        Assert.Equal("key: F5", MacroScript.FormatKeyChord(0x74, Modifiers.None));
    }

    [Fact]
    public void FormatKeyChord_RoundTripsThroughParse()
    {
        var line = MacroScript.FormatKeyChord(0x0D, Modifiers.Alt); // Alt+Enter
        var k = Assert.IsType<KeyStroke>(Assert.Single(MacroScript.Parse(line)));
        Assert.Equal((ushort)0x0D, k.Vk);
        Assert.Equal(Modifiers.Alt, k.Mods);
    }
}
