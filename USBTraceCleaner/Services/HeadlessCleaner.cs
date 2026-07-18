using System.Diagnostics.CodeAnalysis;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

// USBTraceCleaner.exe --clean  (полный forensic-путь, как GUI с максимальными опциями)
[ExcludeFromCodeCoverage]
public static class HeadlessCleaner
{
    public static int Run()
    {
        if (!AdminHelper.IsAdministrator())
        {
            Console.Error.WriteLine("Требуются права администратора.");
            return 1;
        }

        var options = new CleanupOptions
        {
            SimulationMode = false,
            SaveBackup = true,
            BackupPath = AppPaths.GetExeDirectory(),
            CreateRestorePoint = false,
            CloseExplorer = false,
            RebootAfterClean = false,
            CleanAllUsbDevices = false,
            CleanMtpDevices = true,
            ExportFullUsbEnum = false,
            CleanEventLogs = true,
            CleanSystemEventLog = true,
            ScrubLogFiles = true,
            PreserveLogFileTimestamps = true,
            CleanShellBags = true,
            CleanRecentLinks = true,
            CleanBamEntries = true,
            CleanExecutionArtifacts = true,
            CleanExplorerMru = true,
            CleanRecycleBinUsb = true,
            CleanVolumeShadowCopies = true,
            CleanSelfTraces = true,
            CleanOrphanUsbFlags = true,
            CleanAllUsbFlags = true,
            FilterUserAssist = true,
            TryOfflineHiveClean = true,
            ScanPnPGhosts = true,
        };

        var before = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
        Console.WriteLine($"USB-накопители до очистки: {before}");

        Console.WriteLine("Сканирование артефактов...");
        var items = new ArtifactScanner().Scan(options);
        foreach (var item in items)
            item.Selected = true;

        Console.WriteLine($"Найдено элементов: {items.Count}");
        var result = new ArtifactCleaner().ExecuteAsync(items, options).GetAwaiter().GetResult();
        Console.WriteLine(result.Log);

        var after = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
        Console.WriteLine($"USB-накопители после очистки: {after}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.Error.WriteLine(result.ErrorMessage);
            return 1;
        }

        return result.Success ? 0 : 1;
    }
}
