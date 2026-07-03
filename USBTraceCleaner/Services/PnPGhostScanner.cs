using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class PnPGhostScanner
{
    public enum GhostKind
    {
        Duplicate,
        Orphan
    }

    public sealed record GhostEntry(
        string ControlSet,
        string VidKey,
        string Instance,
        string RegistryPath,
        string DeviceInstanceId,
        string? Service,
        GhostKind Kind,
        string? KeeperDeviceId);

    public sealed class GhostCleanupResult
    {
        public int Scanned { get; set; }
        public int GroupsFound { get; set; }
        public int Removed { get; set; }
        public int Failed { get; set; }
    }

    /// <summary>Сканирование без изменений системы.</summary>
    public static List<ArtifactItem> Scan()
    {
        var removable = CollectRemovableEntries();
        return removable
            .Select(ToArtifactItem)
            .OrderBy(i => i.Location, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GhostCleanupResult RemoveAll(Action<string>? log = null) =>
        RemoveEntries(CollectRemovableEntries(), log);

    public static GhostCleanupResult RemoveSelected(IEnumerable<ArtifactItem> items, Action<string>? log = null)
    {
        var paths = items
            .Where(i => i.Category == ArtifactCategory.PnPGhosts && i.Selected)
            .Select(i => i.Location)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = CollectRemovableEntries()
            .Where(e => paths.Contains(e.RegistryPath))
            .ToList();

        return RemoveEntries(entries, log);
    }

    public static bool RemoveGhost(ArtifactItem item, bool simulation, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] GHOST {item.Detail ?? item.Location}");
            return true;
        }

        var entry = FindEntryByPath(item.Location);
        if (entry == null)
        {
            log?.Invoke($"[WARN] GHOST не найден: {item.Location}");
            return !RegistryHelper.KeyExists(RegistryHive.LocalMachine, item.Location);
        }

        return RemoveInstance(entry, log);
    }

    private static GhostCleanupResult RemoveEntries(IReadOnlyList<GhostEntry> entries, Action<string>? log)
    {
        var result = new GhostCleanupResult { Scanned = entries.Count };

        if (!AdminHelper.IsAdministrator())
        {
            log?.Invoke("Требуются права администратора.");
            return result;
        }

        if (entries.Count == 0)
        {
            log?.Invoke("Призраки и дубликаты PnP не найдены.");
            return result;
        }

        result.GroupsFound = entries.Count(e => e.Kind == GhostKind.Duplicate);
        var duplicateGroups = entries.Count(e => e.Kind == GhostKind.Duplicate);
        var orphans = entries.Count - duplicateGroups;

        log?.Invoke($"Найдено к удалению: {entries.Count} (дубликаты: {duplicateGroups}, призраки: {orphans})");
        RegistrySecurityHelper.EnsureDeletePrivileges();

        foreach (var entry in entries)
        {
            if (entry.Kind == GhostKind.Duplicate)
                log?.Invoke($"  Дубликат → удалить, оставить: {entry.KeeperDeviceId}");

            if (RemoveInstance(entry, log))
                result.Removed++;
            else
                result.Failed++;
        }

        log?.Invoke(result.Failed == 0
            ? $"Удалено: {result.Removed}"
            : $"Удалено: {result.Removed}, ошибок: {result.Failed}");

        return result;
    }

    private static List<GhostEntry> CollectRemovableEntries()
    {
        var result = new List<GhostEntry>();
        var usbEntries = CollectEnumUsbInstances();
        var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in GroupDuplicateUsbEntries(usbEntries))
        {
            var list = group.ToList();
            var keeper = ChooseKeeper(list);
            foreach (var entry in list)
            {
                if (IsKeeper(entry, keeper))
                    continue;

                marked.Add(entry.RegistryPath);
                result.Add(entry with
                {
                    Kind = GhostKind.Duplicate,
                    KeeperDeviceId = keeper.DeviceInstanceId
                });
            }
        }

        foreach (var entry in usbEntries)
        {
            if (marked.Contains(entry.RegistryPath))
                continue;

            if (!DeviceUninstallHelper.IsDevicePresent(entry.DeviceInstanceId))
            {
                result.Add(entry with { Kind = GhostKind.Orphan, KeeperDeviceId = null });
            }
        }

        foreach (var storGhost in CollectUsbStorOrphans())
        {
            if (!result.Any(r => r.RegistryPath.Equals(storGhost.RegistryPath, StringComparison.OrdinalIgnoreCase)))
                result.Add(storGhost);
        }

        return result;
    }

    private static IEnumerable<IGrouping<string, GhostEntry>> GroupDuplicateUsbEntries(List<GhostEntry> entries) =>
        entries.GroupBy(e => $"{e.ControlSet}|{UsbInstanceParser.GetDuplicateGroupKey(e.VidKey, e.Instance)}")
            .Where(g => g.Count() > 1);

    private static GhostEntry? FindEntryByPath(string registryPath)
    {
        foreach (var entry in CollectRemovableEntries())
        {
            if (entry.RegistryPath.Equals(registryPath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        if (!registryPath.Contains(@"\Enum\", StringComparison.OrdinalIgnoreCase))
            return null;

        var deviceId = TryBuildDeviceIdFromRegistryPath(registryPath);
        return new GhostEntry(
            ExtractControlSet(registryPath),
            ExtractVidKey(registryPath),
            ExtractInstance(registryPath),
            registryPath,
            deviceId ?? registryPath,
            RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, registryPath, "Service"),
            GhostKind.Orphan,
            null);
    }

    private static List<GhostEntry> CollectEnumUsbInstances()
    {
        var entries = new List<GhostEntry>();

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var usbRoot = $@"SYSTEM\{controlSet}\Enum\USB";
            RegistryHelper.SafeOpen(usbKey =>
            {
                foreach (var vidName in usbKey.GetSubKeyNames())
                {
                    if (vidName.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var vidPath = $@"{usbRoot}\{vidName}";
                    RegistryHelper.SafeOpen(vidRegKey =>
                    {
                        foreach (var instance in vidRegKey.GetSubKeyNames())
                        {
                            var regPath = $@"{vidPath}\{instance}";
                            entries.Add(new GhostEntry(
                                controlSet,
                                vidName,
                                instance,
                                regPath,
                                UsbInstanceParser.ToDeviceInstanceId(vidName, instance),
                                RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, regPath, "Service"),
                                GhostKind.Orphan,
                                null));
                        }
                    }, RegistryHive.LocalMachine, vidPath);
                }
            }, RegistryHive.LocalMachine, usbRoot);
        }

        return entries;
    }

    private static List<GhostEntry> CollectUsbStorOrphans()
    {
        var result = new List<GhostEntry>();

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var storRoot = $@"SYSTEM\{controlSet}\Enum\USBSTOR";
            RegistryHelper.SafeOpen(storKey =>
            {
                foreach (var deviceType in storKey.GetSubKeyNames())
                {
                    var typePath = $@"{storRoot}\{deviceType}";
                    RegistryHelper.SafeOpen(typeKey =>
                    {
                        foreach (var serial in typeKey.GetSubKeyNames())
                        {
                            var regPath = $@"{typePath}\{serial}";
                            var deviceId = $@"USBSTOR\{deviceType}\{serial}";
                            if (DeviceUninstallHelper.IsDevicePresent(deviceId))
                                return;

                            result.Add(new GhostEntry(
                                controlSet,
                                deviceType,
                                serial,
                                regPath,
                                deviceId,
                                "USBSTOR",
                                GhostKind.Orphan,
                                null));
                        }
                    }, RegistryHive.LocalMachine, typePath);
                }
            }, RegistryHive.LocalMachine, storRoot);
        }

        return result;
    }

    private static GhostEntry ChooseKeeper(IReadOnlyList<GhostEntry> group)
    {
        var live = group
            .Where(e => DeviceUninstallHelper.IsDevicePresent(e.DeviceInstanceId))
            .OrderByDescending(e => UsbInstanceParser.GetEnumIndex(e.Instance))
            .ToList();

        if (live.Count > 0)
            return live[0];

        return group
            .OrderByDescending(e => UsbInstanceParser.GetEnumIndex(e.Instance))
            .First();
    }

    private static bool IsKeeper(GhostEntry entry, GhostEntry keeper) =>
        entry.ControlSet == keeper.ControlSet
        && entry.Instance.Equals(keeper.Instance, StringComparison.OrdinalIgnoreCase);

    private static ArtifactItem ToArtifactItem(GhostEntry entry)
    {
        var friendly = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, entry.RegistryPath, "FriendlyName")
                       ?? RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, entry.RegistryPath, "DeviceDesc");

        var kindLabel = entry.Kind switch
        {
            GhostKind.Duplicate => "Дубликат PnP",
            GhostKind.Orphan => "Призрак PnP",
            _ => "PnP"
        };

        var desc = entry.Kind switch
        {
            GhostKind.Duplicate => $"{kindLabel}: лишний instance ID",
            GhostKind.Orphan => $"{kindLabel}: запись без активного устройства",
            _ => kindLabel
        };

        if (!string.IsNullOrWhiteSpace(friendly))
            desc += $" ({friendly})";

        return new ArtifactItem
        {
            Category = ArtifactCategory.PnPGhosts,
            Type = ArtifactType.RegistryKey,
            Location = entry.RegistryPath,
            Description = desc,
            Detail = entry.DeviceInstanceId,
            Selected = true
        };
    }

    private static bool RemoveInstance(GhostEntry entry, Action<string>? log)
    {
        try
        {
            DeviceUninstallHelper.TryRemoveDevice(entry.DeviceInstanceId);

            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, entry.RegistryPath))
            {
                log?.Invoke($"[OK]  GHOST {entry.DeviceInstanceId}");
                return true;
            }

            RegistrySecurityHelper.EnsureDeletePrivileges();
            PrepareKeyTreeForDelete(entry.RegistryPath);

            if (RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, entry.RegistryPath)
                || RegistryHelper.DeleteKey(RegistryHive.LocalMachine, entry.RegistryPath, false, null))
            {
                log?.Invoke($"[OK]  GHOST {entry.DeviceInstanceId}");
                return true;
            }

            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, entry.RegistryPath))
            {
                log?.Invoke($"[OK]  GHOST {entry.DeviceInstanceId}");
                return true;
            }

            log?.Invoke($"  SYSTEM reg delete: {entry.DeviceInstanceId}");
            var systemPaths = ExpandDeletePaths(entry.RegistryPath)
                .Select(p => $"HKLM\\{p}")
                .ToList();
            RegistrySystemHelper.TryBatchRegDeleteAsSystem(systemPaths);

            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, entry.RegistryPath))
            {
                log?.Invoke($"[OK]  GHOST {entry.DeviceInstanceId}");
                return true;
            }

            log?.Invoke($"[FAIL] GHOST {entry.DeviceInstanceId}");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[FAIL] GHOST {entry.DeviceInstanceId}: {ex.Message}");
            return false;
        }
    }

    private static void PrepareKeyTreeForDelete(string path)
    {
        var propsPath = $@"{path}\Properties";
        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, propsPath))
        {
            RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, propsPath);
            RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, propsPath);
        }

        RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, path);
    }

    private static IEnumerable<string> ExpandDeletePaths(string path)
    {
        yield return path;
        var props = $@"{path}\Properties";
        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, props))
            yield return props;
    }

    private static string? TryBuildDeviceIdFromRegistryPath(string path)
    {
        if (path.Contains(@"\Enum\USBSTOR\", StringComparison.OrdinalIgnoreCase))
        {
            var idx = path.IndexOf(@"\Enum\USBSTOR\", StringComparison.OrdinalIgnoreCase);
            var tail = path[(idx + @"\Enum\USBSTOR\".Length)..];
            return $@"USBSTOR\{tail.Replace('/', '\\')}";
        }

        if (path.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase))
        {
            var idx = path.IndexOf(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase);
            var tail = path[(idx + @"\Enum\USB\".Length)..];
            var lastSlash = tail.LastIndexOf('\\');
            if (lastSlash <= 0) return null;
            var vid = tail[..lastSlash];
            var inst = tail[(lastSlash + 1)..];
            return UsbInstanceParser.ToDeviceInstanceId(vid, inst);
        }

        return null;
    }

    private static string ExtractControlSet(string path)
    {
        const string prefix = @"SYSTEM\";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return "ControlSet001";

        var rest = path[prefix.Length..];
        var end = rest.IndexOf('\\');
        return end > 0 ? rest[..end] : rest;
    }

    private static string ExtractVidKey(string path)
    {
        var idx = path.LastIndexOf(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idx = path.LastIndexOf(@"\Enum\USBSTOR\", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;
            var tail = path[(idx + @"\Enum\USBSTOR\".Length)..];
            var slash = tail.IndexOf('\\');
            return slash > 0 ? tail[..slash] : tail;
        }

        var usbTail = path[(idx + @"\Enum\USB\".Length)..];
        var usbSlash = usbTail.IndexOf('\\');
        return usbSlash > 0 ? usbTail[..usbSlash] : usbTail;
    }

    private static string ExtractInstance(string path) => path.Split('\\').Last();
}
