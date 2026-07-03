using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class RegistryExportHelperTests
{
    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk\x", @"SYSTEM\ControlSet001\Enum\USBSTOR")]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBPRINT\foo", @"SYSTEM\ControlSet001\Enum\USBPRINT")]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\1", @"SYSTEM\ControlSet001\Enum\USB")]
    [InlineData(@"SYSTEM\MountedDevices", @"SYSTEM\MountedDevices")]
    public void GetExportRoot_TruncatesToParentKey(string input, string expected)
    {
        Assert.Equal(expected, RegistryExportHelper.GetExportRoot(input));
    }
}
