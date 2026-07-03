using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public sealed class ArtifactScanner
{
    private static readonly string[] UsbStorageServices = ["USBSTOR", "UASPStor"];
    private static readonly string[] MtpServices = ["WUDFWpdMtp", "WUDFRd", "WpdUpFltr"];

    private static readonly (string Path, string Filter, bool DeleteAll)[] StaticControlSetKeys =
    [
        (@"Enum\USBSTOR", "", true),
        (@"Enum\USBPRINT", "", true),
        (@"Services\USBSTOR\Enum", "", true),
        (@"Enum\SWD\WPDBUSENUM", "USBSTOR", false),
        (@"Enum\STORAGE\Volume", "USBSTOR", false),
        (@"Enum\STORAGE\RemovableMedia", "", false),
        (@"Control\Class\{36FC9E60-C465-11CF-8056-444553540000}", "USBSTOR_BULK", false),
        (@"Control\Class\{EEC5AD98-8080-425F-922A-DABF3DE3F69A}", "Basic_Install", false),
        (@"Control\DeviceClasses\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR#Disk", false),
        (@"Control\DeviceClasses\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR#CdRom", false),
        (@"Control\DeviceClasses\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR", false),
        (@"Control\DeviceClasses\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", "STORAGE#RemovableMedia", false),
        (@"Control\DeviceClasses\{6ac27878-a6fa-4155-ba85-f98f491d4f33}", "USBSTOR", false),
        (@"Control\DeviceClasses\{f33fdc04-d1ac-4e8e-9a30-19bbd4b108ae}", "USBSTOR", false),
        (@"Control\DeviceClasses\{10497b1b-ba51-44e5-8318-a65c837b6661}", "USBSTOR", false),
        (@"Control\DeviceClasses\{7fccc86c-228a-40ad-8a58-f590af7bfdce}", "USBSTOR", false),
        (@"Control\DeviceClasses\{7f108a28-9833-4b3b-b780-2c6b5fa5c062}", "USBSTOR", false),
        (@"Control\DeviceClasses\{6ead3d82-25ec-46bc-b7fd-c1f0df8f5037}", "USBSTOR", false),
        (@"Services\rdyboost\AttachState", "", true),
        (@"Services\rdyboost\Enum", "", true),
    ];

    private static readonly string[] StaticHklmKeys =
    [
        @"SYSTEM\Setup\Upgrade",
        @"SYSTEM\Setup\SetupapiLogStatus",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\EMDMgmt",
        @"SOFTWARE\Microsoft\Windows Portable Devices\Devices",
        @"SOFTWARE\Microsoft\Windows Search\VolumeInfoCache",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
    ];

    private static readonly (string SubKey, string ValueContains)[] StaticHklmValues =
    [
        (@"SOFTWARE\Microsoft\WBEM\WDM", "USBSTOR"),
        (@"SOFTWARE\Microsoft\WBEM\WDM\DREDGE", "USBSTOR"),
    ];

    private static readonly string[] PerUserKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\KnownDevices",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Portable Devices",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
        @"SOFTWARE\Microsoft\Windows\ShellNoRoam\MuiCache",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\UserAssist",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\SyncMgr\HandlerInstances",
    ];

    private static readonly string[] ShellBagKeys =
    [
        @"SOFTWARE\Microsoft\Windows\Shell\Bags",
        @"SOFTWARE\Microsoft\Windows\Shell\BagMRU",
        @"SOFTWARE\Microsoft\Windows\ShellNoRoam\Bags",
        @"SOFTWARE\Microsoft\Windows\ShellNoRoam\BagMRU",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\ShellNoRoam\Bags",
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\ShellNoRoam\BagMRU",
    ];

    private static readonly string[] LogFilePatterns =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "setup*.log"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "setupapi*.log"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "setupapi.ev1"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "setupapi.ev2"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "setupapi.ev3"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "INFCACHE.1"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wbem", "Logs", "wmiprov.log"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "appcompat", "pca", "PcaGeneralDb*.txt"),
    ];

    private static readonly string[] EventLogChannels =
    [
        "Microsoft-Windows-DeviceSetupManager/Operational",
        "Microsoft-Windows-DeviceSetupManager/Admin",
        "Microsoft-Windows-Kernel-PnP/Configuration",
        "Microsoft-Windows-Kernel-ShimEngine/Operational",
        "Microsoft-Windows-DriverFrameworks-UserMode/Operational",
        "Microsoft-Windows-Partition/Diagnostic",
        "Microsoft-Windows-Storage-ClassPnP/Operational",
        "Microsoft-Windows-WPD-MTPClassDriver/Operational",
        "Microsoft-Windows-UserPnp/DeviceInstall",
        "HardwareEvents",
    ];

    public List<ArtifactItem> Scan(CleanupOptions options, IProgress<CleanupProgress>? progress = null)
    {
        var items = new ConcurrentBag<ArtifactItem>();
        var driveMask = RegistryHelper.GetConnectedDriveMask();

        progress?.Report(new CleanupProgress { Phase = "Сканирование ControlSet..." });
        ScanControlSets(items, options);

        progress?.Report(new CleanupProgress { Phase = "Сканирование HKLM..." });
        ScanStaticHklm(items);

        progress?.Report(new CleanupProgress { Phase = "Сканирование MountedDevices..." });
        ScanMountedDevices(items, driveMask);

        progress?.Report(new CleanupProgress { Phase = "Сканирование профилей пользователей..." });
        ScanUserProfiles(items, options, driveMask);

        if (options.CleanBamEntries)
        {
            progress?.Report(new CleanupProgress { Phase = "Сканирование BAM/DAM..." });
            ScanBamDam(items, driveMask);
        }

        progress?.Report(new CleanupProgress { Phase = "Сканирование файлов логов..." });
        ScanLogFiles(items);

        progress?.Report(new CleanupProgress { Phase = "Сканирование журналов событий..." });
        ScanEventLogs(items);

        if (options.CleanRecentLinks)
        {
            progress?.Report(new CleanupProgress { Phase = "Сканирование ярлыков Recent..." });
            ScanRecentLinks(items, driveMask);
        }

        if (options.ScanPnPGhosts)
        {
            progress?.Report(new CleanupProgress { Phase = "Поиск призраков и дубликатов PnP..." });
            foreach (var ghost in PnPGhostScanner.Scan())
                items.Add(ghost);
        }

        var result = items
            .GroupBy(i => $"{i.Type}|{i.Location}|{i.ValueName}")
            .Select(g => g.First())
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Location)
            .ToList();

        progress?.Report(new CleanupProgress { Phase = "Готово", ItemsFound = result.Count });
        return result;
    }

    private static void AddKey(ConcurrentBag<ArtifactItem> items, ArtifactCategory cat, string path, string? desc = null)
    {
        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, path) ||
            RegistryHelper.KeyExists(RegistryHive.Users, path) ||
            RegistryHelper.KeyExists(RegistryHive.CurrentUser, path))
        {
            items.Add(new ArtifactItem
            {
                Category = cat,
                Type = ArtifactType.RegistryKey,
                Location = path,
                Description = desc ?? "Ключ реестра"
            });
        }
    }

    private static void AddValue(ConcurrentBag<ArtifactItem> items, ArtifactCategory cat, string path, string valueName, string? desc = null)
    {
        items.Add(new ArtifactItem
        {
            Category = cat,
            Type = ArtifactType.RegistryValue,
            Location = path,
            ValueName = valueName,
            Description = desc ?? "Значение реестра"
        });
    }

    private static void AddFile(ConcurrentBag<ArtifactItem> items, string path, string? desc = null)
    {
        if (File.Exists(path))
        {
            items.Add(new ArtifactItem
            {
                Category = ArtifactCategory.LogFiles,
                Type = ArtifactType.File,
                Location = path,
                Description = desc ?? "Файл лога"
            });
        }
    }

    private void ScanControlSets(ConcurrentBag<ArtifactItem> items, CleanupOptions options)
    {
        var keysToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var prefix = $@"SYSTEM\{controlSet}\";
            ScanDeviceContainers(prefix, keysToDelete);
            ScanEnumUsb(prefix, keysToDelete, options);
            ScanEnumUsbStorDrivers(prefix, keysToDelete);
            ScanDeviceClassesUsb(prefix, keysToDelete);
            ScanStorageVolumes(prefix, keysToDelete);
            ScanStaticControlSetDefs(prefix, keysToDelete);
            DeviceMigrationCleaner.CollectStoragePaths(prefix.TrimEnd('\\'), keysToDelete, options);

            if (options.CleanMtpDevices)
                ScanWpdBusEnum(prefix, keysToDelete);
        }

        foreach (var key in keysToDelete)
            AddKey(items, ArtifactCategory.RegistrySystem, key);
    }

    private static void ScanDeviceContainers(string prefix, HashSet<string> keys)
    {
        var path = prefix + @"Control\DeviceContainers";
        RegistryHelper.SafeOpen(hiveKey =>
        {
            foreach (var containerId in hiveKey.GetSubKeyNames())
            {
                var basePath = $@"{path}\{containerId}\BaseContainers\{containerId}";
                RegistryHelper.SafeOpen(baseKey =>
                {
                    foreach (var valueName in baseKey.GetValueNames())
                    {
                        // Только накопители — USB# трогает камеру/клавиатуру и вызывает пере-перечисление PnP
                        if (valueName.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase) ||
                            valueName.Contains("UASPStor", StringComparison.OrdinalIgnoreCase))
                        {
                            keys.Add($@"{path}\{containerId}");
                            break;
                        }
                    }
                }, RegistryHive.LocalMachine, basePath);
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanEnumUsb(string prefix, HashSet<string> keys, CleanupOptions options)
    {
        var usbPath = prefix + @"Enum\USB";
        var vendorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        RegistryHelper.SafeOpen(usbHive =>
        {
            foreach (var vidPid in usbHive.GetSubKeyNames())
            {
                // Корневые хабы — системные, не трогаем
                if (vidPid.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase)) continue;

                var devicePath = $@"{usbPath}\{vidPid}";
                var subKeysToDelete = new List<string>();
                var totalSubKeys = 0;

                RegistryHelper.SafeOpen(vidKey =>
                {
                    foreach (var instance in vidKey.GetSubKeyNames())
                    {
                        totalSubKeys++;
                        var instancePath = $@"{devicePath}\{instance}";
                        var service = RegistryHelper.GetStringValue(vidKey, instance, "Service");

                        var isStorage = service != null &&
                            UsbStorageServices.Any(s => service.Equals(s, StringComparison.OrdinalIgnoreCase));
                        var isMtp = service != null &&
                            MtpServices.Any(s => service.Equals(s, StringComparison.OrdinalIgnoreCase));
                        var isHid = service != null &&
                            service.Equals("HidUsb", StringComparison.OrdinalIgnoreCase);

                        var shouldDelete = options.CleanAllUsbDevices || isStorage ||
                            (options.CleanMtpDevices && isMtp) ||
                            (options.CleanKeyboardMouse && isHid);

                        if (!shouldDelete) continue;

                        subKeysToDelete.Add(instancePath);
                        vendorIds.Add(vidPid);

                        var containerId = RegistryHelper.GetStringValue(vidKey, instance, "ContainerID");
                        if (!string.IsNullOrEmpty(containerId))
                            keys.Add($@"{prefix}Control\DeviceContainers\{containerId}");

                        var driver = RegistryHelper.GetStringValue(vidKey, instance, "Driver");
                        if (!string.IsNullOrEmpty(driver))
                            keys.Add($@"{prefix}Control\Class\{driver}");
                    }
                }, RegistryHive.LocalMachine, devicePath);

                if (options.CleanAllUsbDevices && totalSubKeys > 0)
                {
                    // Удаляем каждый экземпляр + родительский VID если пуст
                    foreach (var sk in subKeysToDelete)
                        keys.Add(sk);
                    if (subKeysToDelete.Count == totalSubKeys)
                        keys.Add(devicePath);
                }
                else
                {
                    if (totalSubKeys > 0 && subKeysToDelete.Count == totalSubKeys)
                        keys.Add(devicePath);
                    foreach (var sk in subKeysToDelete)
                        keys.Add(sk);
                }
            }
        }, RegistryHive.LocalMachine, usbPath);

        ScanUsbFlagsForVendors(prefix, keys, vendorIds);

        // usbstor control — entries matching known storage VID/PID
        var usbstorPath = prefix + @"Control\usbstor";
        RegistryHelper.SafeOpen(storFlags =>
        {
            foreach (var entry in storFlags.GetSubKeyNames())
            {
                if (vendorIds.Any(v => entry.Contains(v.Replace("VID_", "").Replace("PID_", ""), StringComparison.OrdinalIgnoreCase)))
                    keys.Add($@"{usbstorPath}\{entry}");
            }
        }, RegistryHive.LocalMachine, usbstorPath);
    }

    private static void ScanEnumUsbStorDrivers(string prefix, HashSet<string> keys)
    {
        var path = prefix + @"Enum\USBSTOR";
        RegistryHelper.SafeOpen(storKey =>
        {
            foreach (var deviceType in storKey.GetSubKeyNames())
            {
                var typePath = $@"{path}\{deviceType}";
                RegistryHelper.SafeOpen(typeKey =>
                {
                    foreach (var serial in typeKey.GetSubKeyNames())
                    {
                        var driver = RegistryHelper.GetStringValue(typeKey, serial, "Driver");
                        if (!string.IsNullOrEmpty(driver))
                            keys.Add($@"{prefix}Control\Class\{driver}");
                    }
                }, RegistryHive.LocalMachine, typePath);
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanDeviceClassesUsb(string prefix, HashSet<string> keys)
    {
        var path = prefix + @"Control\DeviceClasses\{a5dcbf10-6530-11d2-901f-00c04fb951ed}";
        RegistryHelper.SafeOpen(classKey =>
        {
            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (!sub.Contains("USB#Vid", StringComparison.OrdinalIgnoreCase)) continue;

                var instance = RegistryHelper.GetStringValue(classKey, sub, "DeviceInstance");
                if (string.IsNullOrEmpty(instance)) continue;

                var enumPath = prefix + @"Enum\" + instance.Replace('#', '\\');

                var service = GetRegistryString(RegistryHive.LocalMachine, enumPath, "Service");
                if (service != null && UsbStorageServices.Any(s => service.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    keys.Add($@"{path}\{sub}");
                    keys.Add(enumPath);
                }
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanStorageVolumes(string prefix, HashSet<string> keys)
    {
        var path = prefix + @"Enum\STORAGE\Volume";
        RegistryHelper.SafeOpen(volKey =>
        {
            foreach (var vol in volKey.GetSubKeyNames())
            {
                if (!vol.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)) continue;
                var driver = RegistryHelper.GetStringValue(volKey, vol, "Driver");
                if (!string.IsNullOrEmpty(driver))
                    keys.Add($@"{prefix}Control\Class\{driver}");
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanWpdBusEnum(string prefix, HashSet<string> keys)
    {
        var path = prefix + @"Enum\SWD\WPDBUSENUM";
        RegistryHelper.SafeOpen(wpdKey =>
        {
            foreach (var device in wpdKey.GetSubKeyNames())
            {
                if (device.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase))
                    keys.Add($@"{path}\{device}");
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanUsbFlagsForVendors(string prefix, HashSet<string> keys, HashSet<string> vendorIds)
    {
        var path = prefix + @"Control\usbflags";
        RegistryHelper.SafeOpen(flagHive =>
        {
            foreach (var entry in flagHive.GetSubKeyNames())
            {
                if (entry.Length < 8) continue;
                var vidPid = $"vid_{entry[..4]}&pid_{entry.Substring(4, 4)}";
                if (vendorIds.Any(v => v.Equals(vidPid, StringComparison.OrdinalIgnoreCase)))
                    keys.Add($@"{path}\{entry}");
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static void ScanStaticControlSetDefs(string prefix, HashSet<string> keys)
    {
        foreach (var (relPath, filter, deleteAll) in StaticControlSetKeys)
        {
            var fullPath = prefix + relPath;
            if (deleteAll)
            {
                if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, fullPath))
                    keys.Add(fullPath);
                continue;
            }

            RegistryHelper.SafeOpen(key =>
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (string.IsNullOrEmpty(filter) || sub.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        keys.Add($@"{fullPath}\{sub}");
                }
            }, RegistryHive.LocalMachine, fullPath);
        }
    }

    private static void ScanStaticHklm(ConcurrentBag<ArtifactItem> items)
    {
        foreach (var key in StaticHklmKeys)
            AddKey(items, ArtifactCategory.RegistrySystem, key);

        foreach (var (subKey, contains) in StaticHklmValues)
        {
            RegistryHelper.SafeOpen(key =>
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var val = key.GetValue(valueName)?.ToString() ?? "";
                    if (val.Contains(contains, StringComparison.OrdinalIgnoreCase))
                        AddValue(items, ArtifactCategory.RegistrySystem, subKey, valueName);
                }
            }, RegistryHive.LocalMachine, subKey);
        }
    }

    private static void ScanMountedDevices(ConcurrentBag<ArtifactItem> items, int driveMask)
    {
        const string path = @"SYSTEM\MountedDevices";
        RegistryHelper.SafeOpen(mdKey =>
        {
            foreach (var valueName in mdKey.GetValueNames())
            {
                var data = mdKey.GetValue(valueName) as byte[];
                if (data == null) continue;

                var text = Encoding.Unicode.GetString(data);
                var isUsb = text.Contains("USBSTOR#Disk", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("USBSTOR#CdRom", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("STORAGE#RemovableMedia", StringComparison.OrdinalIgnoreCase);

                if (!isUsb) continue;

                // Skip connected drive letters
                if (valueName.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase) &&
                    valueName.Length >= 13)
                {
                    var letter = valueName[12];
                    if (RegistryHelper.IsDriveConnected(driveMask, letter)) continue;
                }

                AddValue(items, ArtifactCategory.RegistryMounted, path, valueName, "Том USB в MountedDevices");
            }
        }, RegistryHive.LocalMachine, path);
    }

    private void ScanUserProfiles(ConcurrentBag<ArtifactItem> items, CleanupOptions options, int driveMask)
    {
        foreach (var sid in RegistryHelper.EnumerateUserSids())
        {
            foreach (var relKey in PerUserKeys)
            {
                var path = $@"{sid}\{relKey}";
                AddKey(items, ArtifactCategory.RegistryUser, path);
            }

            if (options.CleanShellBags)
            {
                foreach (var bagKey in ShellBagKeys)
                    AddKey(items, ArtifactCategory.RegistryShell, $@"{sid}\{bagKey}");
            }

            ScanMountPoints2(items, sid, driveMask);
        }
    }

    private static void ScanMountPoints2(ConcurrentBag<ArtifactItem> items, string sid, int driveMask)
    {
        const string mp2 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";
        var fullPath = $@"{sid}\{mp2}";

        RegistryHelper.SafeOpen(mpKey =>
        {
            foreach (var point in mpKey.GetSubKeyNames())
            {
                // Single letter = drive letter mount
                if (point.Length == 1)
                {
                    if (!RegistryHelper.IsDriveConnected(driveMask, point[0]))
                        AddKey(items, ArtifactCategory.RegistryUser, $@"{fullPath}\{point}", "Точка монтирования (буква диска)");
                }
                else
                {
                    AddKey(items, ArtifactCategory.RegistryUser, $@"{fullPath}\{point}", "Точка монтирования (GUID тома)");
                }
            }
        }, RegistryHive.Users, fullPath);

        var cpcPath = $@"{sid}\{mp2}\CPC\Volume";
        RegistryHelper.SafeOpen(cpcKey =>
        {
            foreach (var vol in cpcKey.GetSubKeyNames())
                AddKey(items, ArtifactCategory.RegistryPortable, $@"{cpcPath}\{vol}", "Explorer CPC Volume");
        }, RegistryHive.Users, cpcPath);
    }

    private static void ScanBamDam(ConcurrentBag<ArtifactItem> items, int driveMask)
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
                        foreach (var valueName in userKey.GetValueNames())
                        {
                            if (valueName.Length >= 2 && valueName[1] == ':')
                            {
                                var letter = char.ToUpperInvariant(valueName[0]);
                                if (letter is >= 'D' and <= 'Z' &&
                                    !RegistryHelper.IsDriveConnected(driveMask, letter))
                                {
                                    AddValue(items, ArtifactCategory.RegistrySystem, userPath, valueName,
                                        $"BAM/DAM: запуск с съёмного диска {letter}:");
                                }
                            }
                        }
                    }, RegistryHive.LocalMachine, userPath);
                }
            }, RegistryHive.LocalMachine, statePath);
        }
    }

    private static void ScanLogFiles(ConcurrentBag<ArtifactItem> items)
    {
        foreach (var pattern in LogFilePatterns)
        {
            var dir = Path.GetDirectoryName(pattern)!;
            var filePattern = Path.GetFileName(pattern);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, filePattern))
                AddFile(items, file);
        }
    }

    private static void ScanEventLogs(ConcurrentBag<ArtifactItem> items)
    {
        foreach (var channel in EventLogChannels)
        {
            items.Add(new ArtifactItem
            {
                Category = ArtifactCategory.EventLogs,
                Type = ArtifactType.EventLog,
                Location = channel,
                Description = "Очистка журнала событий"
            });
        }
    }

    private static void ScanRecentLinks(ConcurrentBag<ArtifactItem> items, int driveMask)
    {
        var usersDir = new DirectoryInfo(@"C:\Users");
        if (!usersDir.Exists) return;

        foreach (var userDir in usersDir.EnumerateDirectories())
        {
            if (userDir.Name is "Public" or "Default" or "Default User" or "All Users") continue;

            var recentDir = Path.Combine(userDir.FullName, @"AppData\Roaming\Microsoft\Windows\Recent");
            if (!Directory.Exists(recentDir)) continue;

            foreach (var lnk in Directory.EnumerateFiles(recentDir, "*.lnk"))
            {
                try
                {
                    if (ContainsRemovableDriveReference(lnk, driveMask))
                    {
                        items.Add(new ArtifactItem
                        {
                            Category = ArtifactCategory.FileSystem,
                            Type = ArtifactType.File,
                            Location = lnk,
                            Description = "Ярлык Recent со ссылкой на съёмный носитель"
                        });
                    }
                }
                catch { /* ignore unreadable lnk */ }
            }

            var autoDest = Path.Combine(userDir.FullName, @"AppData\Roaming\Microsoft\Windows\Recent\AutomaticDestinations-ms");
            if (Directory.Exists(autoDest))
            {
                foreach (var jumplist in Directory.EnumerateFiles(autoDest))
                {
                    items.Add(new ArtifactItem
                    {
                        Category = ArtifactCategory.FileSystem,
                        Type = ArtifactType.File,
                        Location = jumplist,
                        Description = "Jump List (AutomaticDestinations) — может содержать USB-пути"
                    });
                }
            }
        }
    }

    private static bool ContainsRemovableDriveReference(string lnkPath, int driveMask)
    {
        var bytes = File.ReadAllBytes(lnkPath);
        var content = Encoding.Unicode.GetString(bytes);

        for (char c = 'D'; c <= 'Z'; c++)
        {
            if (content.Contains($"{c}:\\", StringComparison.OrdinalIgnoreCase) &&
                !RegistryHelper.IsDriveConnected(driveMask, c))
                return true;
        }
        return content.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Removable", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetRegistryString(RegistryHive hive, string subKey, string valueName)
    {
        var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValue(valueName)?.ToString();
    }
}
