using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

/// <summary>
/// Offline-очистка SYSTEM hive (WinPE / копия). На живой системе файл обычно locked —
/// тогда выполняется усиленный live SYSTEM delete + повтор.
/// </summary>
[ExcludeFromCodeCoverage]
public static class OfflineHiveHelper
{
    public static void TryCleanUsbStorFromOfflineOrRetry(
        CleanupOptions options, Action<string>? log = null)
    {
        if (!options.TryOfflineHiveClean)
            return;

        log?.Invoke("--- Offline / усиленный retry USBSTOR & WPDBUSENUM ---");

        if (options.SimulationMode)
        {
            log?.Invoke("[SIM] offline hive / SYSTEM retry");
            return;
        }

        var systemHive = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "config", "SYSTEM");

        var temp = Path.Combine(Path.GetTempPath(), $"USBTC_SYSTEM_{Guid.NewGuid():N}");
        var mounted = false;
        var mountName = $"USBTC_SYS_{Guid.NewGuid():N}";

        try
        {
            if (TryCopyHive(systemHive, temp, log))
            {
                if (RunReg($"load HKLM\\{mountName} \"{temp}\"") == 0)
                {
                    mounted = true;
                    CleanMountedControlSets(mountName, options, log);
                }
                else
                    log?.Invoke("[WARN] reg load offline SYSTEM не удался (ожидаемо на live OS)");
            }
        }
        finally
        {
            if (mounted)
                RunReg($"unload HKLM\\{mountName}");
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        // Усиленный live retry для WPDBUSENUM / USBSTOR
        foreach (var cs in RegistryHelper.EnumerateControlSets())
        {
            foreach (var rel in new[]
                     {
                         $@"SYSTEM\{cs}\Enum\USBSTOR",
                         $@"SYSTEM\{cs}\Enum\SWD\WPDBUSENUM",
                     })
            {
                if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, rel))
                    continue;

                RegistrySecurityHelper.EnsureDeletePrivileges();
                RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, rel);

                if (rel.EndsWith("USBSTOR", StringComparison.OrdinalIgnoreCase))
                {
                    RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, rel);
                    if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, rel))
                        RegistrySystemHelper.TryRegDeleteAsSystem("HKLM", rel);
                    continue;
                }

                // WPDBUSENUM: удалить только USBSTOR-подключи
                RegistryHelper.SafeOpen(key =>
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        if (!sub.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var full = $@"{rel}\{sub}";
                        RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, full);
                        if (!RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, full)
                            && RegistryHelper.KeyExists(RegistryHive.LocalMachine, full))
                        {
                            RegistrySystemHelper.TryRegDeleteAsSystem("HKLM", full);
                        }
                    }
                }, RegistryHive.LocalMachine, rel);
            }
        }

        // Второй проход SYSTEM batch
        var leftovers = new List<string>();
        foreach (var cs in RegistryHelper.EnumerateControlSets())
        {
            var usbStor = $@"SYSTEM\{cs}\Enum\USBSTOR";
            if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, usbStor)
                && RegistryHelper.CountSubKeys(RegistryHive.LocalMachine, usbStor) > 0)
                leftovers.Add($"HKLM\\{usbStor}");

            var wpd = $@"SYSTEM\{cs}\Enum\SWD\WPDBUSENUM";
            RegistryHelper.SafeOpen(key =>
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (sub.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase))
                        leftovers.Add($"HKLM\\{wpd}\\{sub}");
                }
            }, RegistryHive.LocalMachine, wpd);
        }

        if (leftovers.Count > 0)
        {
            log?.Invoke($"  SYSTEM retry batch: {leftovers.Count} путей");
            RegistrySystemHelper.TryBatchRegDeleteAsSystem(leftovers);
            // третья попытка после короткой паузы
            Thread.Sleep(1500);
            RegistrySystemHelper.TryBatchRegDeleteAsSystem(leftovers);
        }
        else
            log?.Invoke("  ✓ USBSTOR/WPDBUSENUM (USB) пусты после retry");

        log?.Invoke("");
    }

    private static void CleanMountedControlSets(string mount, CleanupOptions options, Action<string>? log)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var root = baseKey.OpenSubKey(mount, writable: true);
        if (root == null) return;

        foreach (var cs in root.GetSubKeyNames())
        {
            if (!cs.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase)
                && !cs.Equals("CurrentControlSet", StringComparison.OrdinalIgnoreCase))
                continue;

            var usbStor = $@"{mount}\{cs}\Enum\USBSTOR";
            try
            {
                baseKey.DeleteSubKeyTree(usbStor, throwOnMissingSubKey: false);
                log?.Invoke($"[OK]  offline KEY HKLM\\{usbStor}");
            }
            catch { /* ignore */ }

            var wpd = $@"{mount}\{cs}\Enum\SWD\WPDBUSENUM";
            using var wpdKey = baseKey.OpenSubKey(wpd, writable: true);
            if (wpdKey == null) continue;
            foreach (var sub in wpdKey.GetSubKeyNames())
            {
                if (!sub.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    wpdKey.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                    log?.Invoke($"[OK]  offline KEY HKLM\\{wpd}\\{sub}");
                }
                catch { /* ignore */ }
            }

            if (options.CleanOrphanUsbFlags)
                DeleteOrphanUsbFlagsOffline(baseKey, $@"{mount}\{cs}", log);
        }
    }

    private static void DeleteOrphanUsbFlagsOffline(RegistryKey baseKey, string csPrefix, Action<string>? log)
    {
        var flagsPath = $@"{csPrefix}\Control\usbflags";
        using var flags = baseKey.OpenSubKey(flagsPath, writable: true);
        if (flags == null) return;

        foreach (var name in flags.GetSubKeyNames())
        {
            if (!ForensicTracePatterns.TryParseUsbFlagsName(name, out var vid, out var pid))
                continue;

            var enumPath = $@"{csPrefix}\Enum\USB\VID_{vid}&PID_{pid}";
            using var enumKey = baseKey.OpenSubKey(enumPath);
            var hasInstances = enumKey?.GetSubKeyNames().Length > 0;
            if (hasInstances && !name.StartsWith("Ignore", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                flags.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                log?.Invoke($"[OK]  offline usbflags {name}");
            }
            catch { /* ignore */ }
        }
    }

    private static bool TryCopyHive(string src, string dst, Action<string>? log)
    {
        try
        {
            using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[WARN] Копия SYSTEM hive: {ex.Message}");
            return false;
        }
    }

    private static int RunReg(string args)
    {
        var psi = new ProcessStartInfo("reg.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return -1;
        proc.WaitForExit(120000);
        return proc.ExitCode;
    }
}
