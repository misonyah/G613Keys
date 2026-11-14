using G613Keys.Core.Input;
using Xunit;

namespace G613Keys.Tests;

public class KeyboardHookTests
{
    // The OS hook can't be exercised in a unit test, but the swallow DECISION can.
    [Fact]
    public void ShouldSwallow_DefaultsToFalse()
    {
        using var hook = new KeyboardHook();
        Assert.False(hook.ShouldSwallow(0x41));
    }

    [Fact]
    public void ShouldSwallow_HonoursPredicate()
    {
        using var hook = new KeyboardHook();
        var swallowed = new HashSet<int> { 0x70, 0x71 }; // e.g. F1, F2 default output
        hook.ShouldSwallow = vk => swallowed.Contains(vk);

        Assert.True(hook.ShouldSwallow(0x70));
        Assert.False(hook.ShouldSwallow(0x41));
    }
}
