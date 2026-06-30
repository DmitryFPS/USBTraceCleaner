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
}
