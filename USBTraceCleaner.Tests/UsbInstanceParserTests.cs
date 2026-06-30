using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class UsbInstanceParserTests
{
    [Fact]
    public void TryParseReEnumerationInstance_ParsesFourPartId()
    {
        Assert.True(UsbInstanceParser.TryParseReEnumerationInstance("6&3ad2d465&2&0000", out var hub, out var index, out var slot));
        Assert.Equal("6&3ad2d465", hub);
        Assert.Equal(2, index);
        Assert.Equal("0000", slot);
    }

    [Fact]
    public void GetDuplicateGroupKey_GroupsReEnumeratedInstances()
    {
        var vid = "VID_30C9&PID_00F8&MI_00";
        var key0 = UsbInstanceParser.GetDuplicateGroupKey(vid, "6&3ad2d465&0&0000");
        var key3 = UsbInstanceParser.GetDuplicateGroupKey(vid, "6&3ad2d465&3&0000");
        Assert.Equal(key0, key3);
    }

    [Fact]
    public void GetDuplicateGroupKey_SingleInstanceStillValid()
    {
        var key = UsbInstanceParser.GetDuplicateGroupKey("VID_046D&PID_C077", "5&393a40ce&0&8");
        Assert.Equal("VID_046D&PID_C077|5&393a40ce|8", key);
    }

    [Fact]
    public void ToDeviceInstanceId_FormatsCorrectly()
    {
        var id = UsbInstanceParser.ToDeviceInstanceId("VID_046D&PID_C077", "5&393a40ce&0&8");
        Assert.Equal(@"USB\VID_046D&PID_C077\5&393a40ce&0&8", id);
    }
}
