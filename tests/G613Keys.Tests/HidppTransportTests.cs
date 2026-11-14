using G613Keys.Core.Hidpp;
using Xunit;

namespace G613Keys.Tests;

public class HidppTransportTests
{
    private static byte[] NormalReply(byte[] req)
    {
        // Echo device, feature index, func|swId; add a payload byte.
        var reply = new byte[HidppReport.ShortLength];
        reply[0] = req[0];
        reply[1] = req[1];
        reply[2] = req[2]; // feature index echoed
        reply[3] = req[3]; // func|swId echoed
        reply[4] = 0x42;
        return reply;
    }

    [Fact]
    public void Request_MatchesReplyByFeatureAndSwId()
    {
        var ch = new FakeChannel(HidppReportKind.Short) { OnWrite = NormalReply };
        using var t = new HidppTransport([ch]);
        t.Start();

        var reply = t.Request(HidppReport.Request(HidppReportKind.Short, 0x01, 0x00, 0x00, 0x80, 0x10));

        Assert.NotNull(reply);
        Assert.Equal(0x42, reply!.Params[0]);
        Assert.Single(ch.Writes);
    }

    [Fact]
    public void Request_TimesOut_ReturnsNull()
    {
        var ch = new FakeChannel(HidppReportKind.Short) { OnWrite = _ => null };
        using var t = new HidppTransport([ch]);
        t.Start();

        var reply = t.Request(HidppReport.Request(HidppReportKind.Short, 0x01, 0x00, 0x00), timeoutMs: 100);

        Assert.Null(reply);
    }

    [Fact]
    public void UnmatchedReport_RaisesNotification()
    {
        var ch = new FakeChannel(HidppReportKind.Long);
        using var t = new HidppTransport([ch]);
        HidppReport? got = null;
        using var seen = new ManualResetEventSlim();
        t.Notification += r => { got = r; seen.Set(); };
        t.Start();

        // GKey-style event: feature index 5, func nibble 0, bitmap.
        ch.Inject(new byte[] { 0x11, 0x01, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.True(seen.Wait(1000));
        Assert.Equal(0x05, got!.FeatureIndex);
    }

    [Fact]
    public void MalformedShortRead_DoesNotThrowAndPipeSurvives()
    {
        var ch = new FakeChannel(HidppReportKind.Long);
        using var t = new HidppTransport([ch]);
        using var seen = new ManualResetEventSlim();
        t.Notification += _ => seen.Set();
        t.Start();

        ch.Inject(new byte[] { 0x11, 0x01 });              // malformed — must be dropped
        ch.Inject(new byte[] { 0x11, 0x01, 0x05, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.True(seen.Wait(1000)); // valid report after the bad one still arrived
    }
}
