namespace USBTraceCleaner.Models;

/// <summary>Группы для фильтра в интерфейсе.</summary>
public enum ArtifactViewGroup
{
    All,
    UsbStorage,
    RegistrySystem,
    RegistryUser,
    RegistryMounted,
    RegistryPortable,
    RegistryShell,
    LogFiles,
    EventLogs,
    FileSystem,
    Services,
    PnPGhosts
}
