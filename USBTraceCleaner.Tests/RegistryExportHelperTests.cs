using Microsoft.Win32;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class RegistryExportHelperTests
{
    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk&Ven_X", @"SYSTEM\ControlSet001\Enum\USBSTOR")]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\instance", @"SYSTEM\ControlSet001\Enum\USB")]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBPRINT\Printer", @"SYSTEM\ControlSet001\Enum\USBPRINT")]
    [InlineData(@"SYSTEM\MountedDevices", @"SYSTEM\MountedDevices")]
    public void GetExportRoot_ReturnsCorrectRoot(string input, string expected)
    {
        Assert.Equal(expected, RegistryExportHelper.GetExportRoot(input));
    }

    [Fact]
    public void GetExportRoot_UsbStorNotMatchedAsUsb()
    {
        var path = @"SYSTEM\ControlSet001\Enum\USBSTOR\Disk&Ven_Kingston";
        var root = RegistryExportHelper.GetExportRoot(path);
        Assert.Contains("USBSTOR", root);
        Assert.DoesNotContain("USB\\", root.Replace("USBSTOR", ""));
    }
}
