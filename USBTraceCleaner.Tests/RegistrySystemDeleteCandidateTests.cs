using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class RegistrySystemDeleteCandidateTests
{
    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk&Ven_X", true)]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_048D&PID_C193\abc", true)]
    [InlineData(@"SYSTEM\ControlSet001\Control\usbflags\048DC1930008", true)]
    [InlineData(@"SYSTEM\ControlSet001\Control\DeviceClasses\{a5dcbf10-6530-11d2-901f-00c04fb951ed}\##?#USB", true)]
    [InlineData(@"SYSTEM\ControlSet001\Control\DeviceMigration\Devices\USB\VID_1", true)]
    [InlineData(@"SYSTEM\Setup\Upgrade\PnP\CurrentControlSet\Control\DeviceMigration", true)]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false)]
    public void IsSystemDeleteCandidate(string path, bool expected) =>
        Assert.Equal(expected, RegistryHelper.IsSystemDeleteCandidate(path));
}
