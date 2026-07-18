using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Win32;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class AmcacheCleaner
{
    public static bool Scrub(string amcachePath, bool simulation, Action<string>? log = null)
    {
        if (!File.Exists(amcachePath))
        {
            log?.Invoke($"[SKIP] Amcache не найден: {amcachePath}");
            return true;
        }

        if (simulation)
        {
            log?.Invoke($"[SIM] SCRUB Amcache {amcachePath}");
            return true;
        }

        RegistrySecurityHelper.EnsureDeletePrivileges();
        var tempHive = Path.Combine(Path.GetTempPath(), $"USBTC_Amcache_{Guid.NewGuid():N}.hve");
        var mount = $@"USBTC_AMC_{Guid.NewGuid():N}";

        try
        {
            try
            {
                File.Copy(amcachePath, tempHive, true);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[WARN] Не удалось скопировать Amcache ({ex.Message}) — пробуем VSS/backup copy");
                if (!TryBackupCopy(amcachePath, tempHive, log))
                    return false;
            }

            if (RunReg($"load HKLM\\{mount} \"{tempHive}\"") != 0)
            {
                log?.Invoke("[FAIL] reg load Amcache");
                return false;
            }

            try
            {
                var deleted = 0;
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                foreach (var inventory in new[]
                         {
                             $@"Root\InventoryApplicationFile",
                             $@"InventoryApplicationFile",
                             $@"Root\InventoryApplication",
                         })
                {
                    using var inv = baseKey.OpenSubKey($@"{mount}\{inventory}", writable: true);
                    if (inv == null) continue;

                    foreach (var name in inv.GetSubKeyNames())
                    {
                        using var sub = inv.OpenSubKey(name);
                        var lower = name;
                        var pathVal = sub?.GetValue("LowerCaseLongPath")?.ToString()
                                      ?? sub?.GetValue("Name")?.ToString()
                                      ?? "";
                        var blob = $"{lower} {pathVal}";
                        if (!ForensicTracePatterns.IsUsbOrSelfTrace(blob))
                            continue;

                        try
                        {
                            inv.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                            deleted++;
                        }
                        catch { /* continue */ }
                    }
                }

                log?.Invoke(deleted > 0
                    ? $"[OK]  Amcache: удалено записей {deleted}"
                    : "[OK]  Amcache: релевантных записей не найдено");
            }
            finally
            {
                RunReg($"unload HKLM\\{mount}");
            }

            try
            {
                File.Copy(tempHive, amcachePath, true);
                log?.Invoke("[OK]  Amcache.hve заменён очищенной копией");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[WARN] Amcache очищен во временной копии, замена live-файла не удалась: {ex.Message}");
                return false;
            }
        }
        finally
        {
            try { if (File.Exists(tempHive)) File.Delete(tempHive); } catch { }
        }
    }

    private static bool TryBackupCopy(string src, string dst, Action<string>? log)
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
            log?.Invoke($"[FAIL] Backup-copy Amcache: {ex.Message}");
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
        proc.WaitForExit(60000);
        return proc.ExitCode;
    }
}
