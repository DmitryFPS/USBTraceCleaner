using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class RegistryHelperExtendedTests
{
    [Fact]
    public void GetConnectedDriveMask_IncludesSystemDrive()
    {
        var mask = RegistryHelper.GetConnectedDriveMask();
        Assert.True(mask > 0);
        Assert.True(RegistryHelper.IsDriveConnected(mask, 'C'));
    }

    [Fact]
    public void IsDriveConnected_InvalidLetter_ReturnsFalse()
    {
        Assert.False(RegistryHelper.IsDriveConnected(0xFFFF, '1'));
    }

    [Fact]
    public void AdminHelper_WindowsLabel_IsWin10Or11()
    {
        Assert.True(AdminHelper.IsWindows10Or11());
        var label = AdminHelper.GetWindowsVersionLabel();
        Assert.Contains("Windows", label);
    }

    [Fact]
    public void CountUsbDeviewDevices_DoesNotThrow()
    {
        var count = RegistryHelper.CountUsbDeviewDevices();
        Assert.True(count >= 0);
    }

    [Fact]
    public void VolumeHelper_HasMountedUsbVolumes_DoesNotThrow()
    {
        _ = VolumeHelper.HasMountedUsbVolumes();
    }
}
