using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class OtherUsbPathCollector
{
    private static readonly Regex VidPidRegex = new(
        @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] StorageServices = ["USBSTOR", "UASPStor"];

    private static readonly string[] DeviceMigrationRoots =
    [
        @"SYSTEM\Setup\Upgrade\PnP\CurrentControlSet\Control\DeviceMigration",
        @"SYSTEM\Setup\Upgrade\PnP\CurrentControlSet\Control\DeviceMigration\Devices\USB",
        @"SYSTEM\Setup\Upgrade\Pnp\CurrentControlSet\Control\DeviceMigration",
        @"SYSTEM\Setup\Upgrade\Control\DeviceMigration",
    ];

    public static HashSet<string> CollectRegistryPaths(string vid, string pid)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var needle = $"VID_{vid.ToUpperInvariant()}&PID_{pid.ToUpperInvariant()}";

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var prefix = $@"SYSTEM\{controlSet}";
            CollectUsbFlags(prefix, vid, pid, paths);
            CollectEnumUsb(prefix, needle, paths);
            CollectDeviceMigration($@"{prefix}\Control\DeviceMigration", needle, paths);
            CollectDeviceMigration($@"{prefix}\Control\DeviceMigration\Devices\USB", needle, paths);
            CollectDeviceClasses(prefix, needle, paths);
        }

        foreach (var root in DeviceMigrationRoots)
            CollectDeviceMigration(root, needle, paths);

        CollectSetupUpgradeMatches(needle, paths);

        return paths;
    }

    public static List<string> CollectSetupApiLogs(string vid, string pid)
    {
        var result = new List<string>();
        var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf");
        if (!Directory.Exists(infDir)) return result;

        var needle1 = $"VID_{vid}".ToUpperInvariant();
        var needle2 = $"PID_{pid}".ToUpperInvariant();

        foreach (var pattern in new[] { "setupapi*.log", "setupapi.ev*" })
        {
            foreach (var file in Directory.EnumerateFiles(infDir, pattern))
            {
                try
                {
                    if (FileContainsVidPid(file, needle1, needle2))
                        result.Add(file);
                }
                catch { /* ignore */ }
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool FileContainsVidPid(string path, string vid, string pid)
    {
        foreach (var line in File.ReadLines(path))
        {
            var upper = line.ToUpperInvariant();
            if (upper.Contains(vid, StringComparison.Ordinal) && upper.Contains(pid, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void CollectUsbFlags(string prefix, string vid, string pid, HashSet<string> paths)
    {
        var root = $@"{prefix}\Control\usbflags";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        var compact = $"{vid}{pid}".ToUpperInvariant();
        foreach (var name in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            var upper = name.ToUpperInvariant();
            if (upper.StartsWith(compact, StringComparison.Ordinal)
                || upper.EndsWith(compact, StringComparison.Ordinal))
                paths.Add($@"{root}\{name}");
        }
    }

    private static void CollectEnumUsb(string prefix, string needle, HashSet<string> paths)
    {
        var usbRoot = $@"{prefix}\Enum\USB";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, usbRoot))
            return;

        foreach (var vidPidName in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, usbRoot))
        {
            if (!vidPidName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            var vidPidPath = $@"{usbRoot}\{vidPidName}";
            foreach (var instance in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, vidPidPath))
            {
                var instancePath = $@"{vidPidPath}\{instance}";
                if (IsStorageInstance(instancePath))
                    continue;

                paths.Add(instancePath);

                var containerId = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "ContainerID");
                if (!string.IsNullOrEmpty(containerId))
                    paths.Add($@"{prefix}\Control\DeviceContainers\{containerId}");

                var driver = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Driver");
                if (!string.IsNullOrEmpty(driver))
                    paths.Add($@"{prefix}\Control\Class\{driver}");
            }
        }
    }

    private static void CollectDeviceMigration(string root, string needle, HashSet<string> paths)
    {
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        foreach (var path in RegistryQueryHelper.EnumerateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsStoragePath(path))
                continue;
            paths.Add(path);
        }
    }

    private static void CollectDeviceClasses(string prefix, string needle, HashSet<string> paths)
    {
        const string deviceClassGuid = @"{a5dcbf10-6530-11d2-901f-00c04fb951ed}";
        var root = $@"{prefix}\Control\DeviceClasses\{deviceClassGuid}";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        foreach (var sub in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!sub.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;

            var subPath = $@"{root}\{sub}";
            var instance = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, subPath, "DeviceInstance") ?? "";
            if (string.IsNullOrWhiteSpace(instance))
                continue;

            var enumPath = $@"{prefix}\Enum\{instance.Replace('#', '\\')}";
            if (IsStorageInstance(enumPath))
                continue;

            paths.Add(subPath);
            if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, enumPath))
                paths.Add(enumPath);
        }
    }

    private static void CollectSetupUpgradeMatches(string needle, HashSet<string> paths)
    {
        const string root = @"SYSTEM\Setup\Upgrade";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        foreach (var path in RegistryQueryHelper.EnumerateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsStoragePath(path))
                continue;
            paths.Add(path);
        }
    }

    private static bool IsStorageInstance(string path)
    {
        if (path.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)
            || path.Contains("UASPStor", StringComparison.OrdinalIgnoreCase))
            return true;

        var service = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, path, "Service");
        return !string.IsNullOrEmpty(service)
               && StorageServices.Any(s => service.Equals(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStoragePath(string path) =>
        StorageTracePatterns.MatchesStorage(path);

    public static bool MatchesVidPid(string text, string vid, string pid)
    {
        var match = VidPidRegex.Match(text);
        if (!match.Success) return false;
        return match.Groups[1].Value.Equals(vid, StringComparison.OrdinalIgnoreCase)
               && match.Groups[2].Value.Equals(pid, StringComparison.OrdinalIgnoreCase);
    }
}
