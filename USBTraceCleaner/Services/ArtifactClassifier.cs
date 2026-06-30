using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

public static class ArtifactClassifier
{
    public static ArtifactViewGroup Classify(ArtifactItem item)
    {
        if (item.Category == ArtifactCategory.PnPGhosts)
            return ArtifactViewGroup.PnPGhosts;

        if (IsUsbStorageTrace(item))
            return ArtifactViewGroup.UsbStorage;

        return item.Category switch
        {
            ArtifactCategory.RegistrySystem => ArtifactViewGroup.RegistrySystem,
            ArtifactCategory.RegistryUser => ArtifactViewGroup.RegistryUser,
            ArtifactCategory.RegistryMounted => ArtifactViewGroup.RegistryMounted,
            ArtifactCategory.RegistryPortable => ArtifactViewGroup.RegistryPortable,
            ArtifactCategory.RegistryShell => ArtifactViewGroup.RegistryShell,
            ArtifactCategory.LogFiles => ArtifactViewGroup.LogFiles,
            ArtifactCategory.EventLogs => ArtifactViewGroup.EventLogs,
            ArtifactCategory.FileSystem => ArtifactViewGroup.FileSystem,
            ArtifactCategory.Services => ArtifactViewGroup.Services,
            _ => ArtifactViewGroup.RegistrySystem
        };
    }

    public static bool IsUsbStorageTrace(ArtifactItem item)
    {
        var path = item.Location;
        var text = $"{path} {item.ValueName} {item.Description} {item.Detail}";

        if (path.Contains(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Services\USBSTOR\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Control\usbflags\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Control\usbstor\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Control\DeviceMigration", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Enum\USBPRINT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)
            || text.Contains("UASPStor", StringComparison.OrdinalIgnoreCase))
            return true;

        if (item.Category == ArtifactCategory.RegistryMounted)
            return true;

        if (path.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(@"ROOT_", StringComparison.OrdinalIgnoreCase)
            && IsLikelyStorageEnumInstance(path))
            return true;

        if (path.Contains(@"\SOFTWARE\Microsoft\WBEM\WDM", StringComparison.OrdinalIgnoreCase))
            return text.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase);

        if (path.Contains(@"\Windows Portable Devices\Devices", StringComparison.OrdinalIgnoreCase))
            return false;

        return false;
    }

    private static bool IsLikelyStorageEnumInstance(string path)
    {
        var service = RegistryHelper.GetStringValueAt(
            Microsoft.Win32.RegistryHive.LocalMachine, path, "Service");
        if (string.IsNullOrEmpty(service)) return false;

        return service.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase)
               || service.Equals("UASPStor", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetGroupLabel(ArtifactViewGroup group) => group switch
    {
        ArtifactViewGroup.All => "Все",
        ArtifactViewGroup.UsbStorage => "USB-флешки и диски",
        ArtifactViewGroup.RegistrySystem => "Реестр (SYSTEM)",
        ArtifactViewGroup.RegistryUser => "Реестр (пользователи)",
        ArtifactViewGroup.RegistryMounted => "MountedDevices",
        ArtifactViewGroup.RegistryPortable => "Portable / MTP",
        ArtifactViewGroup.RegistryShell => "Shell / Explorer",
        ArtifactViewGroup.LogFiles => "Файлы логов",
        ArtifactViewGroup.EventLogs => "Журналы событий",
        ArtifactViewGroup.FileSystem => "Файловая система",
        ArtifactViewGroup.Services => "Службы",
        ArtifactViewGroup.PnPGhosts => "Призраки / дубликаты",
        _ => group.ToString()
    };

    public static IReadOnlyList<ArtifactViewGroup> OrderedGroups =>
    [
        ArtifactViewGroup.All,
        ArtifactViewGroup.UsbStorage,
        ArtifactViewGroup.PnPGhosts,
        ArtifactViewGroup.RegistrySystem,
        ArtifactViewGroup.RegistryUser,
        ArtifactViewGroup.RegistryMounted,
        ArtifactViewGroup.RegistryPortable,
        ArtifactViewGroup.RegistryShell,
        ArtifactViewGroup.LogFiles,
        ArtifactViewGroup.EventLogs,
        ArtifactViewGroup.FileSystem,
        ArtifactViewGroup.Services
    ];
}
