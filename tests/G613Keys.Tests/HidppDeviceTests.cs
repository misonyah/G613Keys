using G613Keys.Core.Hidpp;
using Xunit;

namespace G613Keys.Tests;

public class HidppDeviceTests
{
    // 1.0 error reply to a Root.getFeature probe (wire[3]=0x00, wire[4]=0x08 echo
    // the request so IsReplyTo matches; wire[5] is the error code).
    private static byte[] Error10(byte dev, byte code)
        => new byte[] { 0x10, dev, 0x8F, 0x00, 0x08, code, 0x00 };

    // Normal getFeature reply carrying the feature index.
    private static byte[] FeatureReply(byte dev, byte featureIndex)
        => new byte[] { 0x10, dev, 0x00, 0x08, featureIndex, 0x01, 0x02 };

    private static HidppTransport TransportWith(Func<byte, byte[]?> perDevice)
    {
        var ch = new FakeChannel(HidppReportKind.Short)
        {
            OnWrite = wire => perDevice(wire[1]),
        };
        var t = new HidppTransport([ch]);
        t.Start();
        return t;
    }

    [Fact]
    public void FindDeviceIndex_SkipsUnknownDevice_PicksResourceErrorSlot()
    {
        using var t = TransportWith(dev => dev == 3
            ? Error10(dev, HidppError.ResourceError)   // asleep device at slot 3
            : Error10(dev, HidppError.UnknownDevice));  // empty slots

        Assert.Equal((byte)3, HidppDevice.FindDeviceIndex(t));
    }

    [Fact]
    public void FindDeviceIndex_PrefersAwakeSlot()
    {
        using var t = TransportWith(dev => dev switch
        {
            2 => Error10(dev, HidppError.ResourceError), // asleep
            4 => FeatureReply(dev, 0x06),                // awake, answers the feature query
            _ => Error10(dev, HidppError.UnknownDevice),
        });

        Assert.Equal((byte)4, HidppDevice.FindDeviceIndex(t));
    }

    [Fact]
    public void FindDeviceIndex_ReturnsNull_WhenNothingAnswers()
    {
        using var t = TransportWith(_ => null);
        Assert.Null(HidppDevice.FindDeviceIndex(t, perProbeTimeoutMs: 50));
    }

    [Fact]
    public void GetFeatureIndex_ParsesReply()
    {
        using var t = TransportWith(dev => FeatureReply(dev, 0x07));
        var device = new HidppDevice(t, 0x01);
        Assert.Equal((byte)0x07, device.GetFeatureIndex(HidppDevice.GKeysFeatureId));
        Assert.True(device.GKeysSupported);
    }

    [Fact]
    public void GetFeatureIndex_Returns0_WhenUnsupported()
    {
        // Feature index 0 in the reply means "not supported".
        using var t = TransportWith(dev => FeatureReply(dev, 0x00));
        var device = new HidppDevice(t, 0x01);
        Assert.Equal((byte)0, device.GetFeatureIndex(HidppDevice.GKeysFeatureId));
        Assert.False(device.GKeysSupported);
    }
}
