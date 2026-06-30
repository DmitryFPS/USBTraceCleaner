using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

/// <summary>
/// Точечная очистка DeviceMigration — только следы накопителей, без удаления всего ключа
/// (полное удаление вызывает повторное PnP-перечисление встроенных USB-устройств).
/// </summary>
public static class DeviceMigrationCleaner
{
    public static void CollectStoragePaths(string controlSetPrefix, HashSet<string> paths, CleanupOptions options)
    {
        var root = $@"{controlSetPrefix}\Control\DeviceMigration";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectMatchingSubtrees(root, matches, options);
        foreach (var path in matches)
            paths.Add(path);
    }

    public static int CleanStorageEntries(string controlSetPrefix, CleanupOptions options, Action<string>? log = null)
    {
        if (options.SimulationMode) return 0;

        var root = $@"{controlSetPrefix}\Control\DeviceMigration";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return 0;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectStoragePaths(controlSetPrefix, paths, options);

        if (paths.Count == 0)
        {
            log?.Invoke("  DeviceMigration: следов накопителей нет — корень сохранён");
            return 0;
        }

        log?.Invoke($"  DeviceMigration: удаление {paths.Count} ключ(ей) накопителей (корень сохранён)");
        var failed = 0;
        foreach (var path in paths.OrderByDescending(p => p.Length))
        {
            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))
                continue;

            RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, path);
            if (RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, path)
                || RegistryHelper.DeleteKey(RegistryHive.LocalMachine, path, false, log))
            {
                log?.Invoke($"[OK]  KEY HKLM\\{path}");
                continue;
            }

            failed++;
            log?.Invoke($"[FAIL] KEY HKLM\\{path}");
        }

        return failed;
    }

    private static void CollectMatchingSubtrees(string path, HashSet<string> result, CleanupOptions options)
    {
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var sub in key.GetSubKeyNames())
            {
                var subPath = $@"{path}\{sub}";
                if (NodeMatchesStorage(subPath, sub, options))
                    result.Add(subPath);
                else
                    CollectMatchingSubtrees(subPath, result, options);
            }
        }, RegistryHive.LocalMachine, path);
    }

    private static bool NodeMatchesStorage(string path, string name, CleanupOptions options)
    {
        if (StorageTracePatterns.MatchesDeviceMigrationEntry(name, options.CleanMtpDevices))
            return true;

        var matches = false;
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var valueName in key.GetValueNames())
            {
                if (StorageTracePatterns.MatchesDeviceMigrationEntry(valueName, options.CleanMtpDevices))
                {
                    matches = true;
                    return;
                }

                var text = key.GetValue(valueName)?.ToString();
                if (StorageTracePatterns.MatchesDeviceMigrationEntry(text, options.CleanMtpDevices))
                {
                    matches = true;
                    return;
                }
            }
        }, RegistryHive.LocalMachine, path);

        return matches;
    }
}
