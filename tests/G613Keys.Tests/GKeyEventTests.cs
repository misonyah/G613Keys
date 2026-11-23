using G613Keys.Core.GKeys;
using G613Keys.Core.Hidpp;
using Xunit;

namespace G613Keys.Tests;

public class GKeyEventTests
{
    private const byte Feat = 0x05;
    private const byte Dev = 0x01;

    private static byte[] DivertReply(byte[] wire)
    {
        var r = new byte[HidppReport.LongLength];
        r[0] = wire[0]; r[1] = wire[1]; r[2] = wire[2]; r[3] = wire[3]; // echo
        return r;
    }

    private static byte[] GKeyEventReport(uint bitmap)
    {
        var r = new byte[HidppReport.LongLength];
        r[0] = 0x11; r[1] = Dev; r[2] = Feat; r[3] = 0x00; // func nibble 0
        r[4] = (byte)bitmap; r[5] = (byte)(bitmap >> 8); r[6] = (byte)(bitmap >> 16); r[7] = (byte)(bitmap >> 24);
        return r;
    }

    private static (HidppTransport, FakeChannel, GKeyService) Build()
    {
        var ch = new FakeChannel(HidppReportKind.Long) { OnWrite = DivertReply };
        var t = new HidppTransport([ch]);
        t.Start();
        var svc = new GKeyService(t, new HidppDevice(t, Dev), Feat);
        return (t, ch, svc);
    }

    [Theory]
    [InlineData(0x00u, 0x01u, GKey.G1, true)]   // press G1
    [InlineData(0x01u, 0x00u, GKey.G1, false)]  // release G1
    [InlineData(0x00u, 0x20u, GKey.G6, true)]   // press G6
    public void Diff_SingleTransition(uint prev, uint curr, GKey key, bool down)
    {
        var events = GKeyService.Diff(prev, curr).ToList();
        Assert.Single(events);
        Assert.Equal(new GKeyEvent(key, down), events[0]);
    }

    [Fact]
    public void Diff_Chord_EmitsBothDown()
    {
        var events = GKeyService.Diff(0x00, 0x05).ToList(); // G1 + G3
        Assert.Equal(2, events.Count);
        Assert.Contains(new GKeyEvent(GKey.G1, true), events);
        Assert.Contains(new GKeyEvent(GKey.G3, true), events);
    }

    [Fact]
    public void Diff_NoChange_EmitsNothing()
        => Assert.Empty(GKeyService.Diff(0x07, 0x07));

    [Fact]
    public void Enable_SendsDivertOn()
    {
        var (t, ch, svc) = Build();
        using (t) using (svc)
        {
            Assert.True(svc.Enable());
            Assert.True(svc.Enabled);
            var divert = Assert.Single(ch.Writes);
            Assert.Equal(Feat, divert[2]);
            Assert.Equal(0x28, divert[3]); // func 2 | swId 8
            Assert.Equal(0x01, divert[4]); // on
        }
    }

    [Fact]
    public void ReArm_ResendsDivert()
    {
        var (t, ch, svc) = Build();
        using (t) using (svc)
        {
            svc.Enable();
            Assert.True(svc.ReArm());
            Assert.Equal(2, ch.Writes.Count);
            Assert.All(ch.Writes, w => Assert.Equal(0x01, w[4]));
        }
    }

    [Fact]
    public void RefreshDivert_ResendsDivert_WithoutResettingKeyState()
    {
        var (t, ch, svc) = Build();
        using (t) using (svc)
        {
            svc.Enable();
            var events = new List<GKeyEvent>();
            using var seen = new ManualResetEventSlim();
            svc.KeyChanged += e => { lock (events) events.Add(e); seen.Set(); };

            ch.Inject(GKeyEventReport(0x01));      // G1 down -> lastBitmap = 0x01
            Assert.True(seen.Wait(1000));

            int writesBefore = ch.Writes.Count;
            Assert.True(svc.RefreshDivert());       // keep-alive re-sends divert on
            Assert.Equal(writesBefore + 1, ch.Writes.Count);
            Assert.Equal(0x01, ch.Writes[^1][4]);

            // The same bitmap arriving again must NOT re-fire a down: state was kept.
            seen.Reset();
            ch.Inject(GKeyEventReport(0x01));
            Assert.False(seen.Wait(150));
            lock (events) Assert.Single(events);    // still just the one G1 down
        }
    }

    [Fact]
    public void RefreshDivert_WhenNotEnabled_ReturnsFalse()
    {
        var (t, ch, svc) = Build();
        using (t) using (svc)
            Assert.False(svc.RefreshDivert());      // never enabled -> no-op
    }

    [Fact]
    public void Notification_RaisesKeyChanged()
    {
        var (t, ch, svc) = Build();
        using (t) using (svc)
        {
            var events = new List<GKeyEvent>();
            using var seen = new ManualResetEventSlim();
            svc.KeyChanged += e => { lock (events) events.Add(e); seen.Set(); };

            ch.Inject(GKeyEventReport(0x01)); // G1 down

            Assert.True(seen.Wait(1000));
            lock (events)
                Assert.Contains(new GKeyEvent(GKey.G1, true), events);
        }
    }
}
