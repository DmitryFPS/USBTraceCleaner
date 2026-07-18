using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

/// <summary>Prefetch, Amcache, AppCompatCache, Recycle Bin, Explorer MRU.</summary>
[ExcludeFromCodeCoverage]
public static class ExtendedForensicScanner
{
    public static void ScanAll(ConcurrentBag<ArtifactItem> items, CleanupOptions options, int driveMask)
    {
        if (options.CleanExecutionArtifacts)
        {
            ScanPrefetch(items, options);
            ScanAmcache(items, options);
            ScanAppCompatCache(items, options);
        }

        if (options.CleanExplorerMru)
            ScanExplorerMru(items, options, driveMask);
        else if (options.FilterUserAssist)
        {
            foreach (var sid in RegistryHelper.EnumerateUserSids())
                ScanUserAssistFiltered(items, sid, options);
        }

        if (options.CleanRecycleBinUsb)
            ScanRecycleBin(items, driveMask);
    }

    public static void ScanPrefetch(ConcurrentBag<ArtifactItem> items, CleanupOptions options)
    {
        var prefetchDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        if (!Directory.Exists(prefetchDir)) return;

        foreach (var file in Directory.EnumerateFiles(prefetchDir, "*.pf"))
        {
            var name = Path.GetFileName(file);
            var interesting = ForensicTracePatterns.IsPrefetchOfInterest(name);
            if (!interesting && options.CleanSelfTraces)
                interesting = ForensicTracePatterns.ContainsSelfTraceToken(name);

            if (!interesting)
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    var ascii = Encoding.ASCII.GetString(bytes);
                    var utf16 = Encoding.Unicode.GetString(bytes);
                    interesting = ForensicTracePatterns.IsUsbOrSelfTrace(ascii)
                                  || ForensicTracePatterns.IsUsbOrSelfTrace(utf16)
                                  || ForensicTracePatterns.MatchesRemovableDriveLetter(
                                      utf16, RegistryHelper.GetConnectedDriveMask());
                }
                catch { /* locked prefetch */ }
            }

            if (!interesting) continue;

            items.Add(new ArtifactItem
            {
                Category = ArtifactCategory.FileSystem,
                Type = ArtifactType.File,
                Location = file,
                Description = "Prefetch — USB / утилита очистки"
            });
        }
    }

    public static void ScanAmcache(ConcurrentBag<ArtifactItem> items, CleanupOptions options)
    {
        var amcache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "AppCompat", "Programs", "Amcache.hve");
        if (!File.Exists(amcache)) return;

        items.Add(new ArtifactItem
        {
            Category = ArtifactCategory.FileSystem,
            Type = ArtifactType.File,
            Location = amcache,
            Description = options.CleanSelfTraces
                ? "Amcache.hve — очистка Inventory по USB/self-trace"
                : "Amcache.hve — очистка USB-связанных записей",
            Detail = "amcache-scrub"
        });
    }

    public static void ScanAppCompatCache(ConcurrentBag<ArtifactItem> items, CleanupOptions options)
    {
        foreach (var cs in RegistryHelper.EnumerateControlSets())
        {
            var path = $@"SYSTEM\{cs}\Control\Session Manager\AppCompatCache";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(path);
                if (key?.GetValue("AppCompatCache") is not byte[] data || data.Length == 0)
                    continue;

                var text = Encoding.Unicode.GetString(data);
                if (!ForensicTracePatterns.IsUsbOrSelfTrace(text)
                    && !ForensicTracePatterns.MatchesRemovableDriveLetter(
                        text, RegistryHelper.GetConnectedDriveMask()))
                    continue;

                items.Add(new ArtifactItem
                {
                    Category = ArtifactCategory.RegistrySystem,
                    Type = ArtifactType.RegistryValue,
                    Location = path,
                    ValueName = "AppCompatCache",
                    Description = "Shimcache/AppCompatCache содержит USB/self-trace пути"
                });
            }
            catch { /* ignore */ }
        }
    }

    public static void ScanExplorerMru(ConcurrentBag<ArtifactItem> items, CleanupOptions options, int driveMask)
    {
        foreach (var sid in RegistryHelper.EnumerateUserSids())
        {
            ScanRecentDocs(items, sid, driveMask, options);
            ScanTypedPaths(items, sid, options);
            ScanOpenSaveMru(items, sid, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\OpenSavePidlMRU", driveMask, options);
            ScanOpenSaveMru(items, sid, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU", driveMask, options);
            if (options.FilterUserAssist)
                ScanUserAssistFiltered(items, sid, options);
        }
    }

    private static void ScanRecentDocs(ConcurrentBag<ArtifactItem> items, string sid, int driveMask, CleanupOptions options)
    {
        var root = $@"{sid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";
        ScanMruTree(items, RegistryHive.Users, root, driveMask, options, "RecentDocs");
    }

    private static void ScanTypedPaths(ConcurrentBag<ArtifactItem> items, string sid, CleanupOptions options)
    {
        var path = $@"{sid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var valueName in key.GetValueNames())
            {
                var val = key.GetValue(valueName)?.ToString() ?? "";
                if (!ForensicTracePatterns.IsUsbOrSelfTrace(val)
                    && !ForensicTracePatterns.MatchesRemovableDriveLetter(
                        val, RegistryHelper.GetConnectedDriveMask()))
                    continue;

                items.Add(new ArtifactItem
                {
                    Category = ArtifactCategory.RegistryShell,
                    Type = ArtifactType.RegistryValue,
                    Location = path,
                    ValueName = valueName,
                    Description = "TypedPaths — USB / self-trace"
                });
            }
        }, RegistryHive.Users, path);
    }

    private static void ScanOpenSaveMru(
        ConcurrentBag<ArtifactItem> items, string sid, string relative, int driveMask, CleanupOptions options)
    {
        var root = $@"{sid}\{relative}";
        ScanMruTree(items, RegistryHive.Users, root, driveMask, options, "OpenSave/LastVisited MRU");
    }

    private static void ScanMruTree(
        ConcurrentBag<ArtifactItem> items, RegistryHive hive, string root, int driveMask,
        CleanupOptions options, string label)
    {
        if (!RegistryHelper.KeyExists(hive, root)) return;

        void Walk(string path)
        {
            RegistryHelper.SafeOpen(key =>
            {
                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.Equals("MRUListEx", StringComparison.OrdinalIgnoreCase)
                        || valueName.Equals("MRUList", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var raw = key.GetValue(valueName);
                    var text = raw switch
                    {
                        string s => s,
                        byte[] b => Encoding.Unicode.GetString(b),
                        _ => raw?.ToString() ?? ""
                    };

                    if (!ForensicTracePatterns.IsUsbOrSelfTrace(text)
                        && !ForensicTracePatterns.MatchesRemovableDriveLetter(text, driveMask)
                        && !(options.CleanSelfTraces && ForensicTracePatterns.ContainsSelfTraceToken(text)))
                        continue;

                    items.Add(new ArtifactItem
                    {
                        Category = ArtifactCategory.RegistryShell,
                        Type = ArtifactType.RegistryValue,
                        Location = path,
                        ValueName = valueName,
                        Description = $"{label} — USB / self-trace"
                    });
                }

                foreach (var sub in key.GetSubKeyNames())
                    Walk($@"{path}\{sub}");
            }, hive, path);
        }

        Walk(root);
    }

    private static void ScanUserAssistFiltered(ConcurrentBag<ArtifactItem> items, string sid, CleanupOptions options)
    {
        var root = $@"{sid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
        RegistryHelper.SafeOpen(ua =>
        {
            foreach (var guid in ua.GetSubKeyNames())
            {
                var countPath = $@"{root}\{guid}\Count";
                RegistryHelper.SafeOpen(countKey =>
                {
                    foreach (var valueName in countKey.GetValueNames())
                    {
                        var decoded = ForensicTracePatterns.Rot13(valueName);
                        if (!ForensicTracePatterns.IsUsbOrSelfTrace(decoded)
                            && !(options.CleanSelfTraces && ForensicTracePatterns.ContainsSelfTraceToken(decoded)))
                            continue;

                        items.Add(new ArtifactItem
                        {
                            Category = ArtifactCategory.RegistryShell,
                            Type = ArtifactType.RegistryValue,
                            Location = countPath,
                            ValueName = valueName,
                            Description = $"UserAssist — {decoded}"
                        });
                    }
                }, RegistryHive.Users, countPath);
            }
        }, RegistryHive.Users, root);
    }

    public static void ScanRecycleBin(ConcurrentBag<ArtifactItem> items, int driveMask)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var recycle = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(recycle)) continue;

            try
            {
                foreach (var sidDir in Directory.EnumerateDirectories(recycle))
                {
                    foreach (var file in Directory.EnumerateFiles(sidDir, "$I*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(file);
                            var text = Encoding.Unicode.GetString(bytes);
                            if (!ForensicTracePatterns.IsUsbOrSelfTrace(text)
                                && !ForensicTracePatterns.MatchesRemovableDriveLetter(text, driveMask))
                                continue;

                            items.Add(new ArtifactItem
                            {
                                Category = ArtifactCategory.FileSystem,
                                Type = ArtifactType.File,
                                Location = file,
                                Description = "Recycle Bin — removable/USB path"
                            });

                            var rFile = Path.Combine(sidDir, "R" + Path.GetFileName(file)[1..]);
                            if (File.Exists(rFile))
                            {
                                items.Add(new ArtifactItem
                                {
                                    Category = ArtifactCategory.FileSystem,
                                    Type = ArtifactType.File,
                                    Location = rFile,
                                    Description = "Recycle Bin data — paired with $I"
                                });
                            }
                        }
                        catch { /* ignore */ }
                    }
                }
            }
            catch { /* access denied */ }
        }
    }
}
