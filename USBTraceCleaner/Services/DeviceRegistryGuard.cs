using Microsoft.Win32;

namespace USBTraceCleaner.Services;

/// <summary>Checks Enum ancestors and descendants before deleting device registry data.</summary>
internal static class DeviceRegistryGuard
{
    internal static bool ContainsPresentDevice(string path) => ContainsPresentDevice(
        path, DeviceUninstallHelper.IsDevicePresent, GetChildren);

    internal static bool ContainsPresentDevice(string path, Func<string, bool> isPresent,
        Func<string, IEnumerable<string>> children)
    {
        if (path.EndsWith(@"\Enum", StringComparison.OrdinalIgnoreCase)) return true;
        const string marker = @"\Enum\";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var parts = path[(index + marker.Length)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true; // Never delete the entire Enum tree.
        if (parts.Length >= 3)
            return isPresent(string.Join("\\", parts.Take(3)));

        return Visit(path.TrimEnd('\\'), string.Join("\\", parts), parts.Length);

        bool Visit(string registryPath, string id, int depth)
        {
            if (depth == 3) return isPresent(id);
            foreach (var child in children(registryPath))
                if (Visit(registryPath + "\\" + child, id + "\\" + child, depth + 1))
                    return true;
            return false;
        }
    }

    private static IEnumerable<string> GetChildren(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetSubKeyNames() ?? [];
    }
}
