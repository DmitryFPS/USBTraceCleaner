using USBTraceCleaner.Models;

namespace USBTraceCleaner.Tests;

public class OtherUsbTraceItemTests
{
    [Fact]
    public void DisplayProperties_FormatVidPidAndDate()
    {
        var item = new OtherUsbTraceItem
        {
            Vid = "0e0f",
            Pid = "0003",
            FirstConnected = new DateTime(2026, 1, 15, 10, 30, 0)
        };

        Assert.Equal("0E0F", item.DisplayVid);
        Assert.Equal("0003", item.DisplayPid);
        Assert.Equal("15.01.2026 10:30", item.DisplayFirstConnected);
    }

    [Fact]
    public void DisplayLocation_PrefersRegistryOverLogs()
    {
        var item = new OtherUsbTraceItem
        {
            Vid = "0E0F",
            Pid = "0002",
            RegistryPaths = [@"SYSTEM\ControlSet001\Control\usbflags\0E0F00020000"],
            LogFilePaths = [@"C:\Windows\inf\setupapi.dev.log"]
        };

        Assert.Equal(@"SYSTEM\ControlSet001\Control\usbflags\0E0F00020000", item.DisplayLocation);
    }

    [Fact]
    public void DisplayLocation_FallsBackToLog()
    {
        var item = new OtherUsbTraceItem
        {
            Vid = "0E0F",
            Pid = "0002",
            LogFilePaths = [@"C:\Windows\inf\setupapi.dev.log"]
        };

        Assert.Equal(@"C:\Windows\inf\setupapi.dev.log", item.DisplayLocation);
    }
}
