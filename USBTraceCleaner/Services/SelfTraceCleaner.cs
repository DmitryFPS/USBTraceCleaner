using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

/// <summary>Пост-очистка следов самого USBTraceCleaner и смежных утилит.</summary>
[ExcludeFromCodeCoverage]
public static class SelfTraceCleaner
{
    public static void Run(CleanupOptions options, Action<string>? log = null)
    {
        if (!options.CleanSelfTraces)
            return;

        log?.Invoke("--- Self-trace: следы USBTraceCleaner / USBOblivion / смежных утилит ---");

        if (options.SimulationMode)
        {
            log?.Invoke("[SIM] Prefetch/BAM/Recent/JumpLists/UserAssist self-trace");
            return;
        }

        CleanPrefetchSelf(log);
        CleanBamSelf(log);
        CleanRecentSelf(log);
        CleanJumpListsContainingSelf(log);
        CleanTempArtifacts(log);
        log?.Invoke("");
    }

    private static void CleanPrefetchSelf(Action<string>? log)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.pf"))
        {
            if (!ForensicTracePatterns.ContainsSelfTraceToken(Path.GetFileName(file)))
                continue;
            try
            {
                File.Delete(file);
                log?.Invoke($"[OK]  FILE {file}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[FAIL] {file}: {ex.Message}");
            }
        }
    }

    private static void CleanBamSelf(Action<string>? log)
    {
        foreach (var service in new[] { "bam", "dam" })
        {
            var statePath = $@"SYSTEM\CurrentControlSet\Services\{service}\State\UserSettings";
            RegistryHelper.SafeOpen(stateKey =>
            {
                foreach (var userSid in stateKey.GetSubKeyNames())
                {
                    var userPath = $@"{statePath}\{userSid}";
                    RegistryHelper.SafeOpen(userKey =>
                    {
                        foreach (var valueName in userKey.GetValueNames().ToList())
                        {
                            if (!ForensicTracePatterns.ContainsSelfTraceToken(valueName))
                                continue;
                            RegistryHelper.DeleteValue(RegistryHive.LocalMachine, userPath, valueName, false, log);
                        }
                    }, RegistryHive.LocalMachine, userPath);
                }
            }, RegistryHive.LocalMachine, statePath);
        }
    }

    private static void CleanRecentSelf(Action<string>? log)
    {
        var users = new DirectoryInfo(@"C:\Users");
        if (!users.Exists) return;

        foreach (var userDir in users.EnumerateDirectories())
        {
            if (userDir.Name is "Public" or "Default" or "Default User" or "All Users") continue;
            var recent = Path.Combine(userDir.FullName, @"AppData\Roaming\Microsoft\Windows\Recent");
            if (!Directory.Exists(recent)) continue;

            foreach (var lnk in Directory.EnumerateFiles(recent, "*.lnk"))
            {
                try
                {
                    var bytes = File.ReadAllBytes(lnk);
                    var text = Encoding.Unicode.GetString(bytes);
                    if (!ForensicTracePatterns.ContainsSelfTraceToken(text)
                        && !ForensicTracePatterns.ContainsSelfTraceToken(Path.GetFileName(lnk)))
                        continue;
                    File.Delete(lnk);
                    log?.Invoke($"[OK]  FILE {lnk}");
                }
                catch { /* ignore */ }
            }
        }
    }

    private static void CleanJumpListsContainingSelf(Action<string>? log)
    {
        var users = new DirectoryInfo(@"C:\Users");
        if (!users.Exists) return;

        foreach (var userDir in users.EnumerateDirectories())
        {
            if (userDir.Name is "Public" or "Default" or "Default User" or "All Users") continue;
            foreach (var folder in new[] { "AutomaticDestinations", "CustomDestinations" })
            {
                var dir = Path.Combine(userDir.FullName, @"AppData\Roaming\Microsoft\Windows\Recent", folder);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        var text = Encoding.Unicode.GetString(bytes);
                        if (!ForensicTracePatterns.ContainsSelfTraceToken(text))
                            continue;
                        File.Delete(file);
                        log?.Invoke($"[OK]  FILE {file}");
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }

    private static void CleanTempArtifacts(Action<string>? log)
    {
        try
        {
            var temp = Path.GetTempPath();
            foreach (var dir in Directory.EnumerateDirectories(temp, "USBTC_*"))
            {
                try
                {
                    Directory.Delete(dir, true);
                    log?.Invoke($"[OK]  DIR  {dir}");
                }
                catch { /* ignore */ }
            }

            foreach (var file in Directory.EnumerateFiles(temp, "USBTC_*"))
            {
                try
                {
                    File.Delete(file);
                    log?.Invoke($"[OK]  FILE {file}");
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }
}
