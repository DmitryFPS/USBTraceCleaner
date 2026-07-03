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

        var ok = 0;
        var failed = 0;
        foreach (var path in orderedPaths)
        {
            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
            {
                L($"[SKIP] KEY HKLM\\{path} (нет)");
                ok++;
                continue;
            }

            RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, path);
            var propsPath = $@"{path}\Properties";
            if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, propsPath))
            {
                RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, propsPath);
                RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, propsPath);
            }

            if (RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, path)
                || RegistryHelper.DeleteKey(RegistryHive.LocalMachine, path, false, L))
            {
                L($"[OK]  KEY HKLM\\{path}");
                ok++;
            }
            else
            {
                L($"[FAIL] KEY HKLM\\{path}");
                failed++;
            }
        }

        foreach (var item in selected)
        {
            foreach (var logPath in OtherUsbPathCollector.CollectSetupApiLogs(item.Vid, item.Pid))
            {
                if (ScrubSetupApiForDevice(logPath, item.Vid, item.Pid, L))
                    ok++;
                else
                    failed++;
            }
        }

        L("");
        L(failed == 0
            ? "✓ Выбранные следы удалены. Перезагрузите Windows."
            : $"⚠ Ошибок: {failed}. Перезагрузите Windows и повторите при необходимости.");

        return new OtherUsbTraceCleanResult
        {
            Success = failed == 0,
            Processed = ok,
            Failed = failed,
            Log = sb.ToString()
        };
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
