namespace USBTraceCleaner.Models;

using USBTraceCleaner.Services;

public enum ArtifactCategory
{
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

public enum ArtifactType
{
    RegistryKey,
    RegistryValue,
    File,
    EventLog,
    Directory
}

public sealed class ArtifactItem
{
    public required ArtifactCategory Category { get; init; }
    public required ArtifactType Type { get; init; }
    public required string Location { get; init; }
    public string? ValueName { get; init; }
    public string? Description { get; init; }
    public string? Detail { get; init; }
    public bool Selected { get; set; } = true;

    public ArtifactViewGroup ViewGroup => ArtifactClassifier.Classify(this);

    public string DisplayViewGroup => ArtifactClassifier.GetGroupLabel(ViewGroup);

    public string DisplayCategory => Category switch
    {
        ArtifactCategory.RegistrySystem => "Реестр (SYSTEM)",
        ArtifactCategory.RegistryUser => "Реестр (пользователи)",
        ArtifactCategory.RegistryMounted => "MountedDevices",
        ArtifactCategory.RegistryPortable => "Portable Devices / MTP",
        ArtifactCategory.RegistryShell => "Shell / Explorer",
        ArtifactCategory.LogFiles => "Файлы логов",
        ArtifactCategory.EventLogs => "Журналы событий",
        ArtifactCategory.FileSystem => "Файловая система",
        ArtifactCategory.Services => "Службы",
        ArtifactCategory.PnPGhosts => "Призраки / дубликаты PnP",
        _ => Category.ToString()
    };
}

public sealed class CleanupOptions
{
    public bool SimulationMode { get; set; } = false;
    public bool SaveBackup { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = true;
    public bool CloseExplorer { get; set; } = true;
    public bool RebootAfterClean { get; set; } = true;
    public bool CleanMtpDevices { get; set; } = true;
    public bool CleanAllUsbDevices { get; set; } = false;
    public bool CleanKeyboardMouse { get; set; } = false;
    public bool CleanShellBags { get; set; } = true;
    public bool CleanRecentLinks { get; set; } = true;
    public bool CleanBamEntries { get; set; } = true;
    /// <summary>Экспортировать весь Enum\USB в .reg (медленно на больших системах).</summary>
    public bool ExportFullUsbEnum { get; set; } = false;
    public bool CleanEventLogs { get; set; } = true;
    /// <summary>Не удалять лог-файлы, а вычищать содержимое (setupapi.dev.log остаётся на месте).</summary>
    public bool ScrubLogFiles { get; set; } = true;
    /// <summary>Сохранять дату создания и изменения лог-файлов после очистки.</summary>
    public bool PreserveLogFileTimestamps { get; set; } = true;
    /// <summary>Искать призрачные и дублированные записи PnP при сканировании.</summary>
    public bool ScanPnPGhosts { get; set; } = true;
    public string? BackupPath { get; set; }
    public string? LogPath { get; set; }
}

public sealed class CleanupProgress
{
    public string Phase { get; set; } = string.Empty;
    public int ItemsFound { get; set; }
    public int ItemsProcessed { get; set; }
}
