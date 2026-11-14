using G613Keys.Core.GKeys;
using G613Keys.Core.Macros;
using G613Keys.Core.Profiles;
using Xunit;

namespace G613Keys.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public ProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "G613KeysTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "profiles.json");
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultProfile()
    {
        var profiles = new ProfileStore(_path).Load();
        var only = Assert.Single(profiles);
        Assert.True(only.IsDefault);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaultProfile()
    {
        File.WriteAllText(_path, "{ this is not valid json ][");
        var profiles = new ProfileStore(_path).Load();
        var only = Assert.Single(profiles);
        Assert.True(only.IsDefault);
    }

    [Fact]
    public void Save_Then_Load_RoundTripsPolymorphicActions()
    {
        var store = new ProfileStore(_path);
        var profile = new Profile("notepad.exe", new Dictionary<GKey, List<MacroAction>>
        {
            [GKey.G1] = new() { new KeyStroke(0x41, Modifiers.Ctrl), new TextInput("hi"), new Delay(50) },
        });
        store.Save([profile, Profile.EmptyDefault()]);

        var loaded = store.Load();
        var np = Assert.Single(loaded, p => p.Match == "notepad.exe");
        var actions = np.Keys[GKey.G1];
        Assert.Equal(3, actions.Count);
        var ks = Assert.IsType<KeyStroke>(actions[0]);
        Assert.Equal((ushort)0x41, ks.Vk);
        Assert.Equal(Modifiers.Ctrl, ks.Mods);
        Assert.Equal("hi", Assert.IsType<TextInput>(actions[1]).Text);
        Assert.Equal(50, Assert.IsType<Delay>(actions[2]).Ms);
    }

    [Fact]
    public void Resolve_ExactMatch_WinsOverDefault()
    {
        var profiles = new List<Profile>
        {
            Profile.EmptyDefault(),
            new("notepad.exe", new Dictionary<GKey, List<MacroAction>>()),
        };
        Assert.Equal("notepad.exe", ProfileStore.Resolve(profiles, "NOTEPAD.EXE").Match);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsDefault()
    {
        var profiles = new List<Profile>
        {
            Profile.EmptyDefault(),
            new("notepad.exe", new Dictionary<GKey, List<MacroAction>>()),
        };
        Assert.True(ProfileStore.Resolve(profiles, "chrome.exe").IsDefault);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }
}
