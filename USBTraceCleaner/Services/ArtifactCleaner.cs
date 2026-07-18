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
        var itemList = items.Where(i => i.Selected).ToList();
        var result = new CleanupResult { TotalFound = itemList.Count };
        _failCount = 0;

        Log("=== USB Trace Cleaner ===");
        Log($"Режим: {(options.SimulationMode ? "СИМУЛЯЦИЯ (ничего не удаляется!)" : "РЕАЛЬНАЯ ОЧИСТКА")}");
        Log($"Windows: {AdminHelper.GetWindowsVersionLabel()}");
        Log($"Элементов к обработке: {itemList.Count}");
        Log("");

        if (!AdminHelper.IsWindows10Or11())
        {
            result.Success = false;
            result.ErrorMessage = "Программа поддерживает только Windows 10 и Windows 11.";
            return result;
        }

        if (!options.SimulationMode && !AdminHelper.IsAdministrator())
        {
            result.Success = false;
            result.ErrorMessage = "Требуются права администратора. Запустите программу от имени администратора.";
            return result;
        }

        if (options.SimulationMode)
        {
            Log("⚠ Включён режим симуляции — реестр НЕ изменяется!");
            if (!AdminHelper.IsAdministrator())
                Log("⚠ Без прав администратора скан/симуляция могут быть неполными.");
            Log("");
        }

        await Task.Run(() =>
        {
            if (options.CreateRestorePoint && !options.SimulationMode)
                CreateRestorePoint();

            if (options.SaveBackup && !options.SimulationMode)
                BackupArtifacts(itemList, options);

            PrepareExplorerForCleanup(options);

            StopServices(!options.SimulationMode);

            // Главные ключи USB — удаляем первыми (как USBOblivion)
            if (!options.SimulationMode)
            {
                progress?.Report(new CleanupProgress { Phase = "Удаление Enum\\USBSTOR и Enum\\USB..." });
                CoreUsbRegistryCleanup(options);
            }

            // Сначала значения, потом ключи (от глубоких к корневым)
            var values = itemList.Where(i => i.Type == ArtifactType.RegistryValue).ToList();
            var keys = itemList.Where(i => i.Type == ArtifactType.RegistryKey)
                .OrderByDescending(i => i.Location.Length)
                .ToList();
            var others = itemList.Where(i => i.Type is ArtifactType.File or ArtifactType.EventLog).ToList();

            var ordered = values.Cast<ArtifactItem>()
                .Concat(keys)
                .Concat(others)
                .ToList();

            if (options.CleanEventLogs && !options.SimulationMode)
            {
                progress?.Report(new CleanupProgress { Phase = "Очистка журналов событий..." });
                foreach (var item in ordered.Where(i => i.Type == ArtifactType.EventLog))
                    ClearEventLogChannel(item.Location);

                Log("--- Доп. журналы (UserPnp / System) ---");
                EventLogForensicCleaner.ClearExtraChannels(
                    options.CleanSystemEventLog, options.SimulationMode, Log);
                Log("");
            }

            var processed = 0;
            foreach (var item in ordered.Where(i => i.Type != ArtifactType.EventLog))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new CleanupProgress
                {
                    Phase = options.SimulationMode ? "Симуляция" : "Очистка",
                    ItemsFound = ordered.Count,
                    ItemsProcessed = processed
                });

                if (!options.SimulationMode && IsHandledByCoreUsbCleanup(item))
                    continue;

                CleanItem(item, options);
                processed++;
            }

            if (!options.SimulationMode)
            {
                OfflineHiveHelper.TryCleanUsbStorFromOfflineOrRetry(options, Log);

                if (options.CleanVolumeShadowCopies)
                    VolumeShadowCopyCleaner.DeleteAllShadows(false, Log);

                SelfTraceCleaner.Run(options, Log);
            }

            if (!options.SimulationMode)
                VerifyCleanup();

            if (!options.SimulationMode)
            {
                Log("--- Удаление призраков и дубликатов PnP ---");
                var ghostItems = itemList.Where(i => i.Category == ArtifactCategory.PnPGhosts).ToList();
                if (ghostItems.Count > 0)
                    PnPGhostScanner.RemoveSelected(ghostItems, Log);
                else
                    PnPGhostScanner.RemoveAll(Log);
                Log("");
            }

            if (!options.SimulationMode && options.ScrubLogFiles)
            {
                Log("--- Восстановление setupapi.dev.log (если отсутствует) ---");
                LogFileScrubber.EnsureCriticalSetupApiLogs(false, options.PreserveLogFileTimestamps, Log);
                Log("");
            }

            // Полная зачистка System (wevtutil оставляет один 104 — убираем через System.evtx)
            if (!options.SimulationMode && options.CleanEventLogs && options.CleanSystemEventLog)
            {
                Log("--- Полная очистка System (без остаточного Event ID 104) ---");
                var purge = WindowsEventLogBrowser.PurgeSystemLogCompletely(Log);
                Log(purge.Ok
                    ? "[OK]  LOG  System (файл журнала обнулён)"
                    : $"[WARN] LOG  System: {purge.Error}");
                Log("");
            }

            progress?.Report(new CleanupProgress
            {
                Phase = "Завершено",
                ItemsFound = ordered.Count,
                ItemsProcessed = processed
            });

            FinalizeExplorerAndReboot(options);

        }, cancellationToken);

        result.ItemsProcessed = itemList.Count;
        var usbStorRemaining = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
        result.UsbStorRemaining = usbStorRemaining;
        result.Success = options.SimulationMode || usbStorRemaining == 0;
        result.Log = LogOutput;
        result.FailedCount = _failCount;

        if (options.LogPath != null)
            File.WriteAllText(options.LogPath, LogOutput);

        return result;
    }

    /// <summary>
    /// Принудительная очистка основных ключей, которые читает USBDeview.
    /// </summary>
    private void CoreUsbRegistryCleanup(CleanupOptions options)
    {
        Log("--- Основная очистка (USBOblivion) ---");

        if (VolumeHelper.HasMountedUsbVolumes())
        {
            Log("  Шаг 1: отключение USB-дисков...");
            VolumeHelper.OfflineUsbDisks(Log);
        }
        else
            Log("  Шаг 1: USB-диски не подключены — пропуск");

        Log("  Шаг 2: удаление устройств через SetupAPI...");
        DeviceUninstallHelper.UninstallUsbStorDevices(Log);
        Thread.Sleep(1000);

        var controlSets = new List<string>();
        var current = RegistryHelper.GetCurrentControlSetName();
        if (!string.IsNullOrEmpty(current))
            controlSets.Add(current);
        foreach (var cs in RegistryHelper.EnumerateControlSets())
        {
            if (!controlSets.Contains(cs, StringComparer.OrdinalIgnoreCase))
                controlSets.Add(cs);
        }

        var totalRemaining = 0;
        foreach (var controlSet in controlSets)
            totalRemaining += UsboOblivionCleanup.CleanControlSet(controlSet, options, Log);

        var mountFailed = UsboOblivionCleanup.CleanMountedDevices(options, Log);
        totalRemaining += mountFailed;

        Log(totalRemaining == 0
            ? "  ✓ Следы USB-накопителей удалены"
            : $"  ⚠ Не удалено элементов: {totalRemaining} (нужна перезагрузка)");
        Log("");
    }

    /// <summary>Быстрая очистка как USBOblivion — без полного сканирования артефактов.</summary>
    public Task<CleanupResult> ExecuteUsboOblivionAsync(CleanupOptions options, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new CleanupResult();
            _failCount = 0;

            Log("=== USB Trace Cleaner (USBOblivion) ===");
            Log($"Режим: {(options.SimulationMode ? "СИМУЛЯЦИЯ" : "РЕАЛЬНАЯ ОЧИСТКА")}");
            Log($"Windows: {AdminHelper.GetWindowsVersionLabel()}");

            if (!AdminHelper.IsAdministrator())
            {
                result.Success = false;
                result.ErrorMessage = "Требуются права администратора.";
                result.Log = LogOutput;
                return result;
            }

            var before = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
            Log($"USB-накопители до очистки: {before}");
            Log("");

            if (options.SimulationMode)
            {
                result.Success = true;
                result.Log = LogOutput;
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (options.CreateRestorePoint)
                CreateRestorePoint();

            if (options.SaveBackup)
                BackupUsbRegistry(options);

            PrepareExplorerForCleanup(options);

            StopServices(true);
            CoreUsbRegistryCleanup(options);

            Log("--- Удаление призраков и дубликатов PnP ---");
            PnPGhostScanner.RemoveAll(Log);
            Log("");

            var after = RegistryHelper.CountUsbStorageTraceDevices(includeMtp: options.CleanMtpDevices);
            Log($"USB-накопители после очистки: {after}");

            FinalizeExplorerAndReboot(options);

            result.Success = after == 0;
            result.Log = LogOutput;
            if (options.ScrubLogFiles)
            {
                Log("--- Восстановление setupapi.dev.log (если отсутствует) ---");
                LogFileScrubber.EnsureCriticalSetupApiLogs(false, options.PreserveLogFileTimestamps, Log);
            }
            if (options.LogPath != null)
                File.WriteAllText(options.LogPath, LogOutput);
            return result;
        }, cancellationToken);
    }

    private void BackupUsbRegistry(CleanupOptions options)
    {
        var exeDir = options.BackupPath ?? AppPaths.GetExeDirectory();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupFile = Path.Combine(exeDir, $"USBTraceCleaner_backup_{timestamp}.reg");
        Log($"Создание резервной копии: {backupFile}");

        var exports = new List<(string HivePrefix, string SubKey)>();
        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            exports.Add(("HKLM", $@"SYSTEM\{controlSet}\Enum\USBSTOR"));
            exports.Add(("HKLM", $@"SYSTEM\{controlSet}\Enum\USB"));
        }
        exports.Add(("HKLM", @"SYSTEM\MountedDevices"));
        RegistryHelper.MergeRegExports(exports.Distinct(), backupFile);
    }

    private void VerifyCleanup()
    {
        Log("--- Проверка после очистки ---");
        var storageCount = RegistryHelper.CountUsbStorageTraceDevices();
        Log(storageCount == 0
            ? "  ✓ Следы USB-накопителей в реестре не найдены"
            : $"  ⚠ Осталось следов USB-накопителей: {storageCount}");

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var usbStor = $@"SYSTEM\{controlSet}\Enum\USBSTOR";
            var count = RegistryHelper.CountSubKeys(RegistryHive.LocalMachine, usbStor);
            var exists = RegistryHelper.KeyExists(RegistryHive.LocalMachine, usbStor);
            if (!exists)
                Log($"  ✓ USBSTOR [{controlSet}]: ключ отсутствует (очищен или не создавался)");
            else if (count == 0)
                Log($"  ✓ USBSTOR [{controlSet}]: пусто");
            else
                Log($"  ⚠ Осталось в USBSTOR [{controlSet}]: {count} записей");
        }

        const string wpdDevices = @"SOFTWARE\Microsoft\Windows Portable Devices\Devices";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, wpdDevices))
        {
            Log("  ✓ WPD Devices: ключ отсутствует");
        }
        else
        {
            var wpdLeft = 0;
            RegistryHelper.SafeOpen(key =>
            {
                foreach (var child in key.GetSubKeyNames())
                {
                    if (ForensicTracePatterns.IsWindowsPortableDeviceUsbChild(child))
                        wpdLeft++;
                }
            }, RegistryHive.LocalMachine, wpdDevices);

            Log(wpdLeft == 0
                ? "  ✓ WPD Devices: USB/WPD-записи накопителей не найдены (ключ сохранён)"
                : $"  ⚠ WPD Devices: осталось USB/WPD-записей: {wpdLeft}");
        }

        if (_failCount > 0)
            Log($"  ⚠ Не удалось удалить {_failCount} элементов — см. [FAIL] выше");
        else
            Log("  ✓ Все элементы обработаны успешно");

        Log("  → Перезагрузите Windows для применения изменений!");
        Log("");
    }

    private static bool IsHandledByCoreUsbCleanup(ArtifactItem item)
    {
        if (item.Category == ArtifactCategory.PnPGhosts)
            return false;

        if (item.Type is not (ArtifactType.RegistryKey or ArtifactType.RegistryValue))
            return false;

        return item.Location.Contains(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase)
               || item.Location.Contains(@"\Services\USBSTOR\", StringComparison.OrdinalIgnoreCase)
               || item.Location.Contains(@"\Control\usbflags\", StringComparison.OrdinalIgnoreCase)
               || item.Location.Contains(@"\Control\DeviceMigration", StringComparison.OrdinalIgnoreCase)
               || (item.Location.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase)
                   && !item.Location.Contains(@"ROOT_", StringComparison.OrdinalIgnoreCase))
               || item.Location.Contains(@"\Control\DeviceContainers\", StringComparison.OrdinalIgnoreCase)
               || (item.Location.Contains(@"\Control\DeviceClasses\", StringComparison.OrdinalIgnoreCase)
                   && item.Location.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase));
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
                    if (!simulation)
                        ClearEventLogChannel(item.Location);
                    Log($"{(simulation ? "[SIM]" : "[OK]")} LOG  {item.Location}");
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
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupFile = Path.Combine(exeDir, $"USBTraceCleaner_backup_{timestamp}.reg");

        Log($"Создание резервной копии: {backupFile}");

        var exports = new List<(string HivePrefix, string SubKey)>();
        var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Всегда бэкапим главные USB-ключи
        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            exports.Add(("HKLM", $@"SYSTEM\{controlSet}\Enum\USBSTOR"));
            if (options.ExportFullUsbEnum)
                exports.Add(("HKLM", $@"SYSTEM\{controlSet}\Enum\USB"));
        }
        exports.Add(("HKLM", @"SYSTEM\MountedDevices"));

        foreach (var item in items.Where(i => i.Type is ArtifactType.RegistryKey or ArtifactType.RegistryValue))
        {
            var hivePrefix = item.Location.StartsWith("S-1-5-") ? "HKU" : "HKLM";
            var rootKey = RegistryExportHelper.GetExportRoot(item.Location);
            var key = $"{hivePrefix}\\{rootKey}";
            if (exported.Add(key))
                exports.Add((hivePrefix, rootKey));
        }

        RegistryHelper.MergeRegExports(exports.Distinct(), backupFile);

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

    private static void StopServices(bool real)
    {
        if (!real) return;
        foreach (var svc in new[] { "vds", "SysMain", "EMDMgmt", "WMPNetworkSvc" })
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(svc);
                if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                    continue;
                sc.Stop();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            catch { /* service may not exist or stop slowly */ }
        }
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

    private void FinalizeExplorerAndReboot(CleanupOptions options)
    {
        if (options.SimulationMode)
            return;

        if (options.RebootAfterClean)
        {
            Log("  Перезагрузка Windows — Проводник не перезапускается (снижает дубликаты USB).");
            RebootSystem();
            return;
        }

        if (options.CloseExplorer)
            StartExplorer();
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

    private static void ClearEventLogChannel(string channel)
    {
        try
        {
            WindowsEventLogBrowser.ClearChannel(channel);
        }
        catch { /* channel may not exist */ }
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
