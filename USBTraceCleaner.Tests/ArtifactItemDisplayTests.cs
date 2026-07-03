using USBTraceCleaner.Models;

namespace USBTraceCleaner.Tests;

public class ArtifactItemDisplayTests
{
    private static ArtifactItem Key(string location) => new()
    {
        Category = ArtifactCategory.RegistrySystem,
        Type = ArtifactType.RegistryKey,
        Location = location
    };

    [Fact]
    public void VidPid_FromEnumUsbPath()
    {
        var item = Key(@"SYSTEM\ControlSet001\Enum\USB\VID_0E0F&PID_0003\6&abc&0&1");
        Assert.Equal("0E0F", item.Vid);
        Assert.Equal("0003", item.Pid);
        Assert.Equal("0E0F", item.DisplayVid);
        Assert.Equal("0003", item.DisplayPid);
    }

    [Fact]
    public void VidPid_FromUsbFlagsPath()
    {
        var item = Key(@"SYSTEM\ControlSet001\Control\usbflags\0E0F00030000");
        Assert.Equal("0E0F", item.Vid);
        Assert.Equal("0003", item.Pid);
    }

    [Fact]
    public void DisplayManufacturer_UsesDatabase()
    {
        var item = Key(@"SYSTEM\ControlSet001\Enum\USB\VID_0E0F&PID_0003\x");
        Assert.Equal("VMware, Inc.", item.DisplayManufacturer);
        Assert.Equal("Virtual Mouse", item.DisplayModel);
    }

    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\x", @"Enum\USB")]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk\x", "USBSTOR")]
    [InlineData(@"SYSTEM\ControlSet001\Control\usbflags\0E0F00030000", "usbflags")]
    [InlineData(@"SYSTEM\ControlSet001\Control\DeviceMigration\USB\VID_1234&PID_5678", "DeviceMigration")]
    [InlineData(@"SYSTEM\MountedDevices", "MountedDevices")]
    public void DisplaySource_MapsRegistrySection(string location, string expected)
    {
        Assert.Equal(expected, Key(location).DisplaySource);
    }

    [Fact]
    public void DisplaySource_FileAndEventLog()
    {
        var file = new ArtifactItem
        {
            Category = ArtifactCategory.LogFiles,
            Type = ArtifactType.File,
            Location = @"C:\Windows\inf\setupapi.dev.log"
        };
        Assert.Equal("Файл", file.DisplaySource);

        var ev = new ArtifactItem
        {
            Category = ArtifactCategory.EventLogs,
            Type = ArtifactType.EventLog,
            Location = "Microsoft-Windows-Kernel-PnP/Configuration"
        };
        Assert.Equal("Журнал событий", ev.DisplaySource);
    }

    [Fact]
    public void DisplayVidPid_DashWhenMissing()
    {
        var item = Key(@"SYSTEM\ControlSet001\Services\USBSTOR\Enum");
        Assert.Equal("—", item.DisplayVid);
        Assert.Equal("—", item.DisplayPid);
        Assert.Equal("—", item.DisplayManufacturer);
    }

    [Theory]
    [InlineData(ArtifactCategory.RegistrySystem, "Реестр (SYSTEM)")]
    [InlineData(ArtifactCategory.RegistryUser, "Реестр (пользователи)")]
    [InlineData(ArtifactCategory.RegistryMounted, "MountedDevices")]
    [InlineData(ArtifactCategory.RegistryPortable, "Portable Devices / MTP")]
    [InlineData(ArtifactCategory.RegistryShell, "Shell / Explorer")]
    [InlineData(ArtifactCategory.LogFiles, "Файлы логов")]
    [InlineData(ArtifactCategory.EventLogs, "Журналы событий")]
    [InlineData(ArtifactCategory.FileSystem, "Файловая система")]
    [InlineData(ArtifactCategory.Services, "Службы")]
    [InlineData(ArtifactCategory.PnPGhosts, "Призраки / дубликаты PnP")]
    public void DisplayCategory_AllValues(ArtifactCategory category, string expected)
    {
        var item = new ArtifactItem
        {
            Category = category,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\test"
        };
        Assert.Equal(expected, item.DisplayCategory);
    }

    [Theory]
    [InlineData(@"SYSTEM\MountPoints2\{guid}", "MountPoints2")]
    [InlineData(@"SYSTEM\Control\DeviceClasses\{guid}", "DeviceClasses")]
    [InlineData(@"SYSTEM\Control\DeviceContainers\{guid}", "DeviceContainers")]
    [InlineData(@"SYSTEM\Enum\SWD\WPDBUSENUM\device", "WPD/MTP")]
    public void DisplaySource_MoreRegistrySections(string location, string expected)
    {
        Assert.Equal(expected, Key(location).DisplaySource);
    }

    [Fact]
    public void DisplaySource_Directory_ReturnsCatalogLabel()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.FileSystem,
            Type = ArtifactType.Directory,
            Location = @"C:\Temp\usb_traces"
        };
        Assert.Equal("Каталог", item.DisplaySource);
    }

    [Fact]
    public void DisplayFirstConnected_NoRegistryValue_ReturnsDash()
    {
        var item = Key(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\no-such-instance");
        Assert.Equal("—", item.DisplayFirstConnected);
    }

    [Fact]
    public void DisplayModel_DashWhenPidMissing()
    {
        var item = new ArtifactItem
        {
            Category = ArtifactCategory.RegistrySystem,
            Type = ArtifactType.RegistryKey,
            Location = @"SYSTEM\ControlSet001\Services\USBSTOR\Enum"
        };
        Assert.Equal("—", item.DisplayModel);
    }

    [Fact]
    public void DisplayViewGroup_DelegatesToClassifier()
    {
        var item = Key(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk");
        Assert.Equal("USB-флешки и диски", item.DisplayViewGroup);
    }

    [Fact]
    public void CleanupOptions_Defaults()
    {
        var o = new CleanupOptions();
        Assert.True(o.SimulationMode == false);
        Assert.True(o.SaveBackup);
        Assert.True(o.ScanPnPGhosts);
    }
}
