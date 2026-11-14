using G613Keys.Core.Macros;
using G613Keys.Core.Native;
using Xunit;
using static G613Keys.Core.Native.Win32Input;

namespace G613Keys.Tests;

public class MacroEngineTests
{
    private sealed class Recorder
    {
        public readonly List<INPUT> Inputs = new();
        public readonly List<int> Delays = new();

        public uint Send(INPUT[] inputs) { Inputs.AddRange(inputs); return (uint)inputs.Length; }
        public Task Delay(int ms, CancellationToken ct) { Delays.Add(ms); return Task.CompletedTask; }
    }

    private static (ushort vk, bool up, bool unicode) Decode(INPUT i)
        => (i.u.ki.wVk, (i.u.ki.dwFlags & KEYEVENTF_KEYUP) != 0, (i.u.ki.dwFlags & KEYEVENTF_UNICODE) != 0);

    [Fact]
    public async Task KeyStroke_WithModifiers_EmitsDownUpInOrder()
    {
        var rec = new Recorder();
        var engine = new MacroEngine(rec.Send, rec.Delay);

        await engine.Run([new KeyStroke(0x41, Modifiers.Ctrl | Modifiers.Shift)]);

        var seq = rec.Inputs.Select(Decode).ToArray();
        Assert.Equal(new (ushort, bool, bool)[]
        {
            (0x11, false, false), // Ctrl down
            (0x10, false, false), // Shift down
            (0x41, false, false), // A down
            (0x41, true,  false), // A up
            (0x10, true,  false), // Shift up
            (0x11, true,  false), // Ctrl up
        }, seq);
    }

    [Fact]
    public async Task TextInput_EmitsUnicodeDownUpPerChar()
    {
        var rec = new Recorder();
        var engine = new MacroEngine(rec.Send, rec.Delay);

        await engine.Run([new TextInput("Hi")]);

        Assert.Equal(4, rec.Inputs.Count);
        Assert.All(rec.Inputs, i => Assert.True((i.u.ki.dwFlags & KEYEVENTF_UNICODE) != 0));
        Assert.Equal('H', (char)rec.Inputs[0].u.ki.wScan);
        Assert.Equal('i', (char)rec.Inputs[2].u.ki.wScan);
    }

    [Fact]
    public async Task Delay_AwaitsInjectedDelay()
    {
        var rec = new Recorder();
        var engine = new MacroEngine(rec.Send, rec.Delay);

        await engine.Run([new Delay(75)]);

        Assert.Equal(new[] { 75 }, rec.Delays);
    }

    [Fact]
    public async Task Repeat_RunsBodyNTimes()
    {
        var rec = new Recorder();
        var engine = new MacroEngine(rec.Send, rec.Delay);

        await engine.Run([new Repeat([new KeyStroke(0x42)], 3)]);

        // Each KeyStroke with no modifiers = down+up = 2 inputs.
        Assert.Equal(6, rec.Inputs.Count);
    }

    [Fact]
    public async Task Run_Cancelled_Throws()
    {
        var rec = new Recorder();
        var engine = new MacroEngine(rec.Send, rec.Delay);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.Run([new KeyStroke(0x41)], cts.Token));
        Assert.Empty(rec.Inputs);
    }
}
