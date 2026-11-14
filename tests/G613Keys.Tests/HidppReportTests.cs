using G613Keys.Core.Hidpp;
using Xunit;

namespace G613Keys.Tests;

public class HidppReportTests
{
    [Fact]
    public void Request_PacksFuncIndexIntoHighNibbleWithSwId()
    {
        var r = HidppReport.Request(HidppReportKind.Short, 0x01, 0x00, 0x00, 0x80, 0x10);
        Assert.Equal(0x08, r.FuncSwId); // func 0 -> 0x00, swId 0x08
        Assert.Equal(0, r.FuncIndex);
        Assert.Equal(HidppReport.SwId, r.SoftwareId);

        var divert = HidppReport.Request(HidppReportKind.Long, 0x01, 0x05, 0x02, 0x01);
        Assert.Equal(0x28, divert.FuncSwId); // func 2 -> 0x20 | 0x08
        Assert.Equal(2, divert.FuncIndex);
    }

    [Fact]
    public void ToWire_Short_PacksFields()
    {
        var r = HidppReport.Request(HidppReportKind.Short, 0x01, 0x00, 0x00, 0x80, 0x10);
        var wire = r.ToWire(HidppReport.ShortLength);
        Assert.Equal(new byte[] { 0x10, 0x01, 0x00, 0x08, 0x80, 0x10, 0x00 }, wire);
    }

    [Fact]
    public void Parse_RoundTripsFields()
    {
        var wire = new byte[] { 0x10, 0x02, 0x00, 0x08, 0x05, 0x01, 0x02 };
        var r = HidppReport.Parse(wire);
        Assert.Equal(0x10, r.ReportId);
        Assert.Equal(0x02, r.DeviceIndex);
        Assert.Equal(0x00, r.FeatureIndex);
        Assert.Equal(0x08, r.FuncSwId);
        Assert.Equal(new byte[] { 0x05, 0x01, 0x02 }, r.Params);
    }

    [Fact]
    public void Parse_10Error_ExposesResourceErrorCode()
    {
        // 1.0 error: [reportId, dev, 0x8F, subId, address, code]
        var wire = new byte[] { 0x10, 0x01, 0x8F, 0x00, 0x00, HidppError.ResourceError, 0x00 };
        var r = HidppReport.Parse(wire);
        Assert.True(r.Is10Error);
        Assert.Equal(HidppError.ResourceError, r.ErrorCode);
    }

    [Fact]
    public void Parse_20Error_ExposesErrorCode()
    {
        // 2.0 error: [reportId, dev, 0xFF, feat, func|swId, code]
        var wire = new byte[] { 0x11, 0x01, 0xFF, 0x05, 0x28, HidppError.UnknownDevice, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var r = HidppReport.Parse(wire);
        Assert.True(r.Is20Error);
        Assert.Equal(HidppError.UnknownDevice, r.ErrorCode);
    }
}
