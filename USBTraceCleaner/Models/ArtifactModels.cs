namespace USBTraceCleaner.Models;

using System.Text.RegularExpressions;
using Microsoft.Win32;
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
    private static readonly Regex VidPidRegex = new(
        @"V(?:ID)?[_]?([0-9A-Fa-f]{4})[&#_]{1,2}P(?:ID)?[_]?([0-9A-Fa-f]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // usbflags: ключ вида 0E0F00020000 (VID+PID+ревизия, 12 hex).
    private static readonly Regex UsbFlagsRegex = new(
        @"\\usbflags\\([0-9A-Fa-f]{4})([0-9A-Fa-f]{4})[0-9A-Fa-f]{0,4}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Guid FirstInstallProperty =
        new("83da6326-97a6-4088-9453-a1923f573b29");

    private string? _vid;
    private string? _pid;
    private bool _idsParsed;
    private bool _firstConnectedRead;
    private DateTime? _firstConnected;

    public required ArtifactCategory Category { get; init; }
    public required ArtifactType Type { get; init; }
    public required string Location { get; init; }
    public string? ValueName { get; init; }
    public string? Description { get; init; }
    public string? Detail { get; init; }
    public bool Selected { get; set; } = true;

    private void EnsureIds()
    {
        if (_idsParsed) return;
        _idsParsed = true;

        var flags = UsbFlagsRegex.Match(Location);
        if (flags.Success)
        {
            _vid = flags.Groups[1].Value.ToUpperInvariant();
            _pid = flags.Groups[2].Value.ToUpperInvariant();
            return;
        }

        var m = VidPidRegex.Match(Location);
        if (m.Success)
        {
            _vid = m.Groups[1].Value.ToUpperInvariant();
            _pid = m.Groups[2].Value.ToUpperInvariant();
        }
    }

    public string? Vid { get { EnsureIds(); return _vid; } }
    public string? Pid { get { EnsureIds(); return _pid; } }

    public string DisplayVid => Vid ?? "—";
    public string DisplayPid => Pid ?? "—";

    public string DisplayManufacturer =>
        Vid != null ? UsbVendorDatabase.LookupVendor(Vid) ?? "—" : "—";

    public string DisplayModel =>
        (Vid != null && Pid != null ? UsbVendorDatabase.LookupProduct(Vid, Pid) : null) ?? "—";

    public DateTime? FirstConnected
    {
        get
        {
            if (_firstConnectedRead) return _firstConnected;
            _firstConnectedRead = true;
            _firstConnected = ReadFirstInstallDate();
            return _firstConnected;
        }
    }

    public string DisplayFirstConnected =>
        FirstConnected?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    public string DisplaySource
    {
        get
        {
            var loc = Location;
            if (loc.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase)) return @"Enum\USB";
            if (loc.Contains(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase)) return "USBSTOR";
            if (loc.Contains(@"\usbflags", StringComparison.OrdinalIgnoreCase)) return "usbflags";
            if (loc.Contains("DeviceMigration", StringComparison.OrdinalIgnoreCase)) return "DeviceMigration";
            if (loc.Contains("DeviceClasses", StringComparison.OrdinalIgnoreCase)) return "DeviceClasses";
            if (loc.Contains("DeviceContainers", StringComparison.OrdinalIgnoreCase)) return "DeviceContainers";
            if (loc.Contains("MountedDevices", StringComparison.OrdinalIgnoreCase)) return "MountedDevices";
            if (loc.Contains("MountPoints2", StringComparison.OrdinalIgnoreCase)) return "MountPoints2";
            if (loc.Contains("WPDBUSENUM", StringComparison.OrdinalIgnoreCase)) return "WPD/MTP";
            return Type switch
            {
                ArtifactType.File => "Файл",
                ArtifactType.EventLog => "Журнал событий",
                ArtifactType.Directory => "Каталог",
                _ => DisplayCategory
            };
        }
    }

    private DateTime? ReadFirstInstallDate()
    {
        if (Type is not (ArtifactType.RegistryKey or ArtifactType.RegistryValue)) return null;
        if (!Location.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase)) return null;

        var propertyKey = $@"{Location}\Properties\{FirstInstallProperty:B}";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(propertyKey);
            if (key?.GetValue("0064") is not byte[] raw || raw.Length < 8) return null;
            var fileTime = BitConverter.ToInt64(raw, 0);
            if (fileTime <= 0) return null;
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch { return null; }
    }

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
    /// <summary>По умолчанию выкл.: точка восстановления сама оставляет артефакт USBTraceCleaner.</summary>
    public bool CreateRestorePoint { get; set; } = false;
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

    /// <summary>Prefetch / Amcache / AppCompatCache (Shimcache).</summary>
    public bool CleanExecutionArtifacts { get; set; } = true;
    /// <summary>RecentDocs, TypedPaths, OpenSavePidlMRU, LastVisitedPidlMRU.</summary>
    public bool CleanExplorerMru { get; set; } = true;
    /// <summary>$Recycle.Bin записи с USB/removable путями.</summary>
    public bool CleanRecycleBinUsb { get; set; } = true;
    /// <summary>Удалить все Volume Shadow Copies (иначе setupapi возвращается из VSS).</summary>
    public bool CleanVolumeShadowCopies { get; set; } = true;
    /// <summary>Следы USBTraceCleaner / USBOblivion / USBDeview / USBDetector.</summary>
    public bool CleanSelfTraces { get; set; } = true;
    /// <summary>Очистить журнал System (UserPnp USBSTOR + прошлые Event ID 104).</summary>
    public bool CleanSystemEventLog { get; set; } = true;
    /// <summary>Удалять осиротевшие usbflags и IgnoreHWSerNum* в core-пути.</summary>
    public bool CleanOrphanUsbFlags { get; set; } = true;
    /// <summary>Удалить весь Control\usbflags (пересоздаётся при следующем PnP).</summary>
    public bool CleanAllUsbFlags { get; set; } = true;
    /// <summary>UserAssist — только USB/self-trace значения, не весь ключ.</summary>
    public bool FilterUserAssist { get; set; } = true;
    /// <summary>Offline hive copy + усиленный SYSTEM retry для USBSTOR/WPDBUSENUM.</summary>
    public bool TryOfflineHiveClean { get; set; } = true;

    public string? BackupPath { get; set; }
    public string? LogPath { get; set; }
}

public sealed class CleanupProgress
{
    public string Phase { get; set; } = string.Empty;
    public int ItemsFound { get; set; }
    public int ItemsProcessed { get; set; }
}
