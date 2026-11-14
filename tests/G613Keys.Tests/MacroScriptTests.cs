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
}
