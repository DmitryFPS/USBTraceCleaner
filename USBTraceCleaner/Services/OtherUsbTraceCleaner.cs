using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class OtherUsbTraceCleaner
{
    public static OtherUsbTraceCleanResult Execute(
        IEnumerable<OtherUsbTraceItem> items,
        bool simulation,
        Action<string>? log = null)
    {
        var sb = new StringBuilder();
        void L(string line)
        {
            sb.AppendLine(line);
            log?.Invoke(line);
        }

        if (!AdminHelper.IsAdministrator())
        {
            return new OtherUsbTraceCleanResult
            {
                Success = false,
                ErrorMessage = "Требуются права администратора.",
                Log = sb.ToString()
            };
        }

        var selected = items.Where(i => i.Selected).ToList();
        if (selected.Count == 0)
        {
            return new OtherUsbTraceCleanResult
            {
                Success = false,
                ErrorMessage = "Нет выбранных записей.",
                Log = sb.ToString()
            };
        }

        L("=== Другие USB-следы ===");
        L($"Режим: {(simulation ? "СИМУЛЯЦИЯ" : "УДАЛЕНИЕ")}");
        L($"Устройств: {selected.Count}");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected)
        {
            foreach (var p in OtherUsbPathCollector.CollectRegistryPaths(item.Vid, item.Pid))
                paths.Add(p);
            foreach (var f in OtherUsbPathCollector.CollectSetupApiLogs(item.Vid, item.Pid))
                logFiles.Add(f);
        }

        var orderedPaths = paths.OrderByDescending(p => p.Length).ToList();
        L($"Ключей реестра: {orderedPaths.Count}");
        L($"Файлов логов: {logFiles.Count}");
        L("");

        if (simulation)
        {
            foreach (var path in orderedPaths)
                L($"[SIM] KEY HKLM\\{path}");
            foreach (var logFile in logFiles)
                L($"[SIM] LOG {logFile}");
            return new OtherUsbTraceCleanResult
            {
                Success = true,
                Processed = orderedPaths.Count + logFiles.Count,
                Log = sb.ToString()
            };
        }

        RegistrySecurityHelper.EnsureDeletePrivileges();

        L("--- Отключение / удаление PnP-устройств ---");
        var instanceIds = CollectInstanceIds(orderedPaths);
        if (instanceIds.Count > 0)
        {
            DeviceUninstallHelper.UninstallUsbDevices(instanceIds, L);
            Thread.Sleep(800);
        }
        else
            L("  (инстансов Enum\\USB не найдено)");
        L("");

        L("--- Удаление ключей реестра ---");
        var ok = 0;
        var failedPaths = new List<string>();
        foreach (var path in orderedPaths)
        {
            if (TryDeleteRegistryPath(path, L))
                ok++;
            else
                failedPaths.Add(path);
        }

        if (failedPaths.Count > 0)
        {
            L("");
            L($"--- SYSTEM delete (упало: {failedPaths.Count}) ---");
            var recovered = RetryFailedWithSystem(failedPaths, L);
            ok += recovered;
            failedPaths = failedPaths
                .Where(p => RegistryHelper.KeyExists(RegistryHive.LocalMachine, p))
                .ToList();
        }

        L("");
        L("--- setupapi ---");
        var scrubbed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected)
        {
            foreach (var logPath in OtherUsbPathCollector.CollectSetupApiLogs(item.Vid, item.Pid))
            {
                if (!scrubbed.Add(logPath)) continue;
                if (ScrubSetupApiForDevice(logPath, item.Vid, item.Pid, L))
                    ok++;
                else
                    failedPaths.Add(logPath);
            }
        }

        L("");
        if (failedPaths.Count == 0)
        {
            L("✓ Выбранные следы удалены. Перезагрузите Windows.");
        }
        else
        {
            L($"⚠ Не удалено: {failedPaths.Count}. Часто Access Denied — устройство занято драйвером.");
            L("  Перезагрузите Windows → снова Сканировать → Очистить выбранное.");
            L("  Неудачные пути:");
            foreach (var p in failedPaths.Take(30))
                L($"    • {p}");
            if (failedPaths.Count > 30)
                L($"    … и ещё {failedPaths.Count - 30}");
        }

        return new OtherUsbTraceCleanResult
        {
            Success = failedPaths.Count == 0,
            Processed = ok,
            Failed = failedPaths.Count,
            FailedPaths = failedPaths,
            Log = sb.ToString(),
            Hint = failedPaths.Count > 0
                ? "Перезагрузите Windows и повторите очистку. «Найдено: 0» после частичного удаления не гарантирует полный wipe."
                : null
        };
    }

    private static List<string> CollectInstanceIds(IEnumerable<string> registryPaths)
    {
        var ids = new List<string>();
        foreach (var path in registryPaths)
        {
            // SYSTEM\ControlSet001\Enum\USB\VID_xxxx&PID_yyyy\instance
            const string marker = @"\Enum\";
            var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var relative = path[(idx + marker.Length)..];
            if (!relative.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
                continue;
            if (relative.EndsWith(@"\Properties", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^@"\Properties".Length];

            // Нужен полный instance ID: USB\VID_..\PID_..\serial
            if (relative.Count(c => c == '\\') < 2)
                continue;

            ids.Add(relative);
        }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryDeleteRegistryPath(string path, Action<string> log)
    {
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
        {
            log($"[SKIP] KEY HKLM\\{path} (нет)");
            return true;
        }

        RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, path);
        var propsPath = $@"{path}\Properties";
        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, propsPath))
        {
            RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, propsPath);
            RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, propsPath);
        }

        if (RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, path)
            && !RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
        {
            log($"[OK]  KEY HKLM\\{path}");
            return true;
        }

        if (RegistryHelper.DeleteKey(RegistryHive.LocalMachine, path, false, log)
            && !RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
            return true;

        if (RegistryHelper.IsSystemDeleteCandidate(path)
            && RegistrySystemHelper.TryRegDeleteAsSystem("HKLM", path)
            && !RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
        {
            log($"[OK]  KEY HKLM\\{path} (SYSTEM)");
            return true;
        }

        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
        {
            log($"[OK]  KEY HKLM\\{path} (уже удалён)");
            return true;
        }

        var reason = RegistryNativeHelper.LastErrorCode switch
        {
            5 => "Access Denied (устройство занято / ACL)",
            2 => "не найден",
            0 => "ключ остался после delete",
            _ => $"код {RegistryNativeHelper.LastErrorCode}"
        };
        log($"[FAIL] KEY HKLM\\{path} — {reason}");
        return false;
    }

    private static int RetryFailedWithSystem(List<string> failedPaths, Action<string> log)
    {
        var candidates = failedPaths
            .Where(RegistryHelper.IsSystemDeleteCandidate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .Select(p => $"HKLM\\{p}")
            .ToList();

        if (candidates.Count == 0)
        {
            log("  Нет кандидатов для SYSTEM delete");
            return 0;
        }

        var recovered = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            log($"  Попытка {attempt}/3: {candidates.Count} путей…");
            RegistrySystemHelper.TryBatchRegDeleteAsSystem(candidates);
            Thread.Sleep(400 * attempt);

            var still = new List<string>();
            foreach (var full in candidates)
            {
                var sub = full.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
                    ? full[5..]
                    : full;
                if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, sub))
                    still.Add(full);
                else
                {
                    log($"[OK]  KEY {full} (SYSTEM batch)");
                    recovered++;
                }
            }

            candidates = still;
            if (candidates.Count == 0) break;
        }

        if (candidates.Count > 0)
            log($"  Осталось после SYSTEM: {candidates.Count}");

        return recovered;
    }

    private static bool ScrubSetupApiForDevice(string path, string vid, string pid, Action<string> log)
    {
        if (!File.Exists(path))
        {
            log($"[SKIP] LOG {path} (нет)");
            return true;
        }

        try
        {
            var needle1 = $"VID_{vid}".ToUpperInvariant();
            var needle2 = $"PID_{pid}".ToUpperInvariant();
            var lines = File.ReadAllLines(path);
            var kept = new List<string>(lines.Length);
            var removed = 0;

            foreach (var line in lines)
            {
                var upper = line.ToUpperInvariant();
                if (upper.Contains(needle1, StringComparison.Ordinal) && upper.Contains(needle2, StringComparison.Ordinal))
                {
                    removed++;
                    continue;
                }
                kept.Add(line);
            }

            if (removed == 0)
            {
                log($"[SKIP] LOG {path} (строк не найдено)");
                return true;
            }

            File.WriteAllLines(path, kept);
            log($"[OK]  LOG {path} (удалено строк: {removed})");
            return true;
        }
        catch (Exception ex)
        {
            log($"[FAIL] LOG {path} — {ex.Message}");
            return false;
        }
    }
}
