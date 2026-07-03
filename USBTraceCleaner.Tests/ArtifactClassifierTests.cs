using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class ArtifactClassifierTests
{
    [Fact]
    public void Classify_UsbStorPath_IsUsbStorage()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Enum\USBSTOR\Disk&Ven_Kingston&Prod_DT_101_G2"
        };

        Assert.Equal(ArtifactViewGroup.UsbStorage, item.ViewGroup);
    }

    [Fact]
    public void Classify_ShellBag_IsShellGroup()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistryShell,
            Type = ArtifactType.RegistryKey,
            Location = @"S-1-5-21\SOFTWARE\Microsoft\Windows\Shell\Bags"
        };

        Assert.Equal(ArtifactViewGroup.RegistryShell, item.ViewGroup);
    }

    [Fact]
    public void Classify_PnPGhost_IsPnPGhostsGroup()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.PnPGhosts,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Enum\USB\VID_30C9&PID_00F8&MI_00\6&3ad2d465&0&0000",
            Description = "Дубликат PnP",
            Detail = @"USB\VID_30C9&PID_00F8&MI_00\6&3ad2d465&0&0000"
        };

        Assert.Equal(ArtifactViewGroup.PnPGhosts, item.ViewGroup);
        Assert.Equal("Призраки / дубликаты", item.DisplayViewGroup);
    }

    [Fact]
    public void Classify_SetupLog_IsLogFiles()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.LogFiles,
            Type = ArtifactType.File,
            Location = @"C:\Windows\inf\setupapi.dev.log"
        };

        Assert.Equal(ArtifactViewGroup.LogFiles, item.ViewGroup);
    }

    [Theory]
    [InlineData(ArtifactCategory.RegistryUser, ArtifactViewGroup.RegistryUser)]
    [InlineData(ArtifactCategory.RegistryPortable, ArtifactViewGroup.RegistryPortable)]
    [InlineData(ArtifactCategory.EventLogs, ArtifactViewGroup.EventLogs)]
    [InlineData(ArtifactCategory.FileSystem, ArtifactViewGroup.FileSystem)]
    [InlineData(ArtifactCategory.Services, ArtifactViewGroup.Services)]
    public void Classify_MapsCategories(ArtifactCategory cat, ArtifactViewGroup expected)
    {
        var item = new ArtifactItem
        {
            Category = cat,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\test"
        };
        Assert.Equal(expected, item.ViewGroup);
    }

    [Fact]
    public void GetGroupLabel_AllKnownGroups()
    {
        foreach (var g in ArtifactClassifier.OrderedGroups)
            Assert.False(string.IsNullOrWhiteSpace(ArtifactClassifier.GetGroupLabel(g)));
    }

    [Fact]
    public void IsUsbStorageTrace_MountedDevices()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistryMounted,
            Type = ArtifactType.RegistryValue,
            Location = @"SYSTEM\MountedDevices",
            ValueName = @"\DosDevices\E:"
        };
        Assert.True(ArtifactClassifier.IsUsbStorageTrace(item));
    }

    [Fact]
    public void IsUsbStorageTrace_UsbFlagsPath()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Control\usbflags\0E0F00030000"
        };
        Assert.True(ArtifactClassifier.IsUsbStorageTrace(item));
    }

    [Fact]
    public void IsUsbStorageTrace_UsbPrintAndDeviceMigration()
    {
        var print = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Enum\USBPRINT\Canon"
        };
        Assert.True(ArtifactClassifier.IsUsbStorageTrace(print));

        var migration = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Control\DeviceMigration\USB\VID_1234&PID_5678"
        };
        Assert.True(ArtifactClassifier.IsUsbStorageTrace(migration));
    }

    [Fact]
    public void IsUsbStorageTrace_UaspStorInDescription()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\1",
            Description = "UASPStor device"
        };
        Assert.True(ArtifactClassifier.IsUsbStorageTrace(item));
    }

    [Fact]
    public void IsUsbStorageTrace_PortableDevices_NotStorage()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistryPortable,
            Type = ArtifactType.RegistryKey,
            Location = @"SOFTWARE\Microsoft\Windows Portable Devices\Devices\{guid}"
        };
        Assert.False(ArtifactClassifier.IsUsbStorageTrace(item));
    }

    [Fact]
    public void IsUsbStorageTrace_WbemWithoutUsbstor()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Services\monitor\Enum\0\SOFTWARE\Microsoft\WBEM\WDM"
        };
        Assert.False(ArtifactClassifier.IsUsbStorageTrace(item));
    }

    [Fact]
    public void Classify_UnknownCategory_FallsBackToRegistrySystem()
    {
        var item = new ArtifactItem
        {
            Category = (ArtifactCategory)999,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\test"
        };
        Assert.Equal(ArtifactViewGroup.RegistrySystem, ArtifactClassifier.Classify(item));
    }

    [Fact]
    public void GetGroupLabel_UnknownGroup_ReturnsToString()
    {
        Assert.Equal("999", ArtifactClassifier.GetGroupLabel((ArtifactViewGroup)999));
    }
}
