using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public sealed class ArtifactCleaner
{
    private readonly StringBuilder _log = new();
    private int _failCount;

    public string LogOutput => _log.ToString();

    public async Task<CleanupResult> ExecuteAsync(
        IEnumerable<ArtifactItem> items,
        CleanupOptions options,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _log.Clear();
        _failCount = 0;
        var itemList = items.Where(i => i.Selected)
            .Where(i => i.Type != ArtifactType.EventLog ||
                (options.CleanEventLogs && (!i.Location.Equals("System", StringComparison.OrdinalIgnoreCase)
                    || options.CleanSystemEventLog)))
            .OrderBy(i => i.Type == ArtifactType.RegistryValue ? 0 : 1)
            .ThenByDescending(i => i.Location.Length).ToList();
        var result = new CleanupResult { TotalFound = itemList.Count };
        Log("=== USB Trace Cleaner — выбранные элементы ===");
        Log(options.SimulationMode ? "СИМУЛЯЦИЯ (ничего не удаляется!)" : "РЕАЛЬНАЯ ОЧИСТКА");
        Log($"Элементов к обработке: {itemList.Count}");

        if (!AdminHelper.IsWindows10Or11())
            result.ErrorMessage = "Программа поддерживает только Windows 10 и Windows 11.";
        else if (!options.SimulationMode && !AdminHelper.IsAdministrator())
            result.ErrorMessage = "Требуются права администратора.";

        if (result.ErrorMessage != null)
        {
            result.Log = LogOutput;
            return result;
        }

        // Empty selection must never trigger global cleanup, service stops or a reboot.
        if (itemList.Count == 0)
        {
            result.Success = true;
            Log("Нет выбранных элементов для обработки.");
            result.Log = LogOutput;
            return result;
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.CreateRestorePoint && !options.SimulationMode)
                CreateRestorePoint();
            if (options.SaveBackup && !options.SimulationMode)
                BackupArtifacts(itemList, options);

            try
            {
                PrepareExplorerForCleanup(options);
                foreach (var item in itemList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CleanItem(item, options);
                    result.ItemsProcessed++;
                    progress?.Report(new CleanupProgress
                    {
                        Phase = options.SimulationMode ? "Симуляция" : "Очистка",
                        ItemsFound = itemList.Count,
                        ItemsProcessed = result.ItemsProcessed
                    });
                }
            }
            finally
            {
                // Restore the shell even after cancellation or an unexpected exception.
                if (!options.SimulationMode && options.CloseExplorer && !options.RebootAfterClean)
                    StartExplorer();
            }
        }, cancellationToken);

        result.UsbStorRemaining = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
        result.FailedCount = _failCount;
        // Remaining unselected devices are not a failure of selective cleanup.
        result.Success = _failCount == 0;
        if (!result.Success)
            result.ErrorMessage = $"Не удалось обработать {_failCount} элементов. См. журнал.";
        if (result.Success && !options.SimulationMode && options.RebootAfterClean)
            RebootSystem();
        result.Log = LogOutput;
        if (options.LogPath != null)
            File.WriteAllText(options.LogPath, LogOutput);
        return result;
    }

    /// <summary>Совместимый вход: сначала сканирование, затем обработка найденных элементов.</summary>
    public async Task<CleanupResult> ExecuteUsboOblivionAsync(CleanupOptions options, CancellationToken cancellationToken = default)
    {
        var items = await Task.Run(() => new ArtifactScanner().Scan(options), cancellationToken);
        return await ExecuteAsync(items, options, cancellationToken: cancellationToken);
    }

    private void CleanItem(ArtifactItem item, CleanupOptions options)
    {
        var simulation = options.SimulationMode;
        try
        {
            if (item.Category == ArtifactCategory.PnPGhosts)
            {
                if (!PnPGhostScanner.RemoveGhost(item, simulation, Log))
                    _failCount++;
                return;
            }

            if (!simulation && item.Type is ArtifactType.RegistryKey or ArtifactType.RegistryValue
                && DeviceRegistryGuard.ContainsPresentDevice(item.Location))
            {
                _failCount++;
                Log($"[FAIL] Подключённое устройство защищено: {item.Location}");
                return;
            }

            switch (item.Type)
            {
                case ArtifactType.RegistryKey:
                    var hive = item.Location.StartsWith("S-1-5-")
                        ? RegistryHive.Users
                        : RegistryHive.LocalMachine;
                    if (!RegistryHelper.DeleteKey(hive, item.Location, simulation, Log))
                        _failCount++;
                    break;

                case ArtifactType.RegistryValue:
                    var valHive = item.Location.StartsWith("S-1-5-")
                        ? RegistryHive.Users
                        : RegistryHive.LocalMachine;
                    if (!RegistryHelper.DeleteValue(valHive, item.Location, item.ValueName!, simulation, Log))
                        _failCount++;
                    break;

                case ArtifactType.File:
                    if (string.Equals(item.Detail, "amcache-scrub", StringComparison.OrdinalIgnoreCase)
                        || item.Location.EndsWith("Amcache.hve", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!AmcacheCleaner.Scrub(item.Location, simulation, Log))
                            _failCount++;
                        break;
                    }

                    if (!simulation)
                    {
                        if (options.ScrubLogFiles && LogFileScrubber.IsManagedLogFile(item.Location))
                        {
                            if (!LogFileScrubber.ScrubOrRestore(
                                    item.Location, false, options.PreserveLogFileTimestamps, Log))
                                _failCount++;
                        }
                        else if (File.Exists(item.Location))
                        {
                            File.Delete(item.Location);
                            Log($"[OK]  FILE {item.Location}");
                        }
                    }
                    else
                    {
                        var action = options.ScrubLogFiles && LogFileScrubber.IsManagedLogFile(item.Location)
                            ? "SCRUB" : "DEL";
                        Log($"[SIM] {action} {item.Location}");
                    }
                    break;

                case ArtifactType.EventLog:
                    if (simulation)
                        Log($"[SIM] LOG  {item.Location}");
                    else
                        ClearEventLogChannel(item.Location);
                    break;
                default:
                    _failCount++;
                    Log($"[FAIL] Неподдерживаемый тип {item.Type}: {item.Location}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _failCount++;
            Log($"[ERR] {item.Location}: {ex.Message}");
        }
    }

    private void BackupArtifacts(List<ArtifactItem> items, CleanupOptions options)
    {
        var exeDir = options.BackupPath ?? AppPaths.GetExeDirectory();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N")[..8];
        var backupFile = Path.Combine(exeDir, $"USBTraceCleaner_backup_{timestamp}.reg");

        Directory.CreateDirectory(exeDir);
        Log($"Создание резервной копии: {backupFile}");

        var exports = new List<(string HivePrefix, string SubKey)>();
        var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.Where(i => i.Type is ArtifactType.RegistryKey or ArtifactType.RegistryValue))
        {
            var hivePrefix = item.Location.StartsWith("S-1-5-") ? "HKU" : "HKLM";
            var rootKey = RegistryExportHelper.GetExportRoot(item.Location);
            var key = $"{hivePrefix}\\{rootKey}";
            if (exported.Add(key))
                exports.Add((hivePrefix, rootKey));
        }

        RegistryHelper.MergeRegExports(exports.Distinct(), backupFile, requireComplete: true);

        var manifestFile = Path.Combine(exeDir, $"USBTraceCleaner_manifest_{timestamp}.txt");
        File.WriteAllText(manifestFile,
            string.Join(Environment.NewLine, items.Select(i => $"{i.Type}\t{i.Location}\t{i.ValueName}")));
        Log($"  Манифест: {manifestFile}");
    }

    private static void CreateRestorePoint()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"Checkpoint-Computer -Description 'USBTraceCleaner' -RestorePointType MODIFY_SETTINGS\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(120000);
        }
        catch { /* restore point may fail on Home edition */ }
    }

    private void PrepareExplorerForCleanup(CleanupOptions options)
    {
        if (options.SimulationMode || !options.CloseExplorer)
            return;

        if (options.RebootAfterClean)
        {
            Log("  Проводник не закрывается — запланирована перезагрузка (снижает дубликаты USB).");
            return;
        }

        Log("  Закрытие Проводника...");
        CloseExplorer();
    }

    private static void CloseExplorer()
    {
        foreach (var proc in Process.GetProcessesByName("explorer"))
        {
            try { proc.Kill(); } catch { }
        }
    }

    private static void StartExplorer()
    {
        Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = true
        });
    }

    private void ClearEventLogChannel(string channel)
    {
        var outcome = WindowsEventLogBrowser.ClearChannel(channel);
        if (!outcome.Ok && !outcome.WasSkipped)
            _failCount++;
        Log($"{(outcome.Ok ? "[OK]" : outcome.WasSkipped ? "[SKIP]" : "[FAIL]")} LOG {channel}: {outcome.Error}");
    }

    private static void RebootSystem()
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"USB Trace Cleaner — перезагрузка после очистки\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void Log(string message) => _log.AppendLine(message);
}

public sealed class CleanupResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalFound { get; set; }
    public int ItemsProcessed { get; set; }
    public int FailedCount { get; set; }
    public int UsbStorRemaining { get; set; }
    public string Log { get; set; } = string.Empty;
}
