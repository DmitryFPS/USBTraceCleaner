using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Text;

namespace USBTraceCleaner.Services;

internal static class WindowsDeviceInventory
{
    internal static List<DeviceIdentity> Read()
    {
        var result = new List<DeviceIdentity>();
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        foreach (var bus in new[] { "USB", "USBSTOR", "SWD", "SCSI", "STORAGE" })
        {
            var root = @"SYSTEM\CurrentControlSet\Enum\" + bus;
            foreach (var model in Children(machine, root))
            foreach (var instance in Children(machine, root + "\\" + model))
            {
                var id = $@"{bus}\{model}\{instance}";
                try
                {
                    using var key = machine.OpenSubKey(root + "\\" + model + "\\" + instance);
                    if (key == null) continue;
                    var name = DeviceIdentityResolver.ReadableName(key.GetValue("FriendlyName") as string)
                        ?? DeviceIdentityResolver.ReadableName(key.GetValue("DeviceDesc") as string)
                        ?? DeviceIdentityResolver.NameFromInstanceId(id) ?? "Устройство без названия";
                    bool? present;
                    try { present = DeviceUninstallHelper.IsDevicePresent(id); } catch { present = null; }
                    result.Add(new DeviceIdentity(id, name, key.GetValue("ContainerID") as string,
                        key.GetValue("Driver") as string, present, key.GetValue("Service") as string));
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return result;
    }

    internal static void Enrich(IEnumerable<ArtifactItem> items, IEnumerable<DeviceIdentity>? inventory = null)
    {
        var resolver = new DeviceIdentityResolver(inventory ?? Read());
        foreach (var item in items)
        {
            string? data = null;
            if (item.Type == ArtifactType.RegistryValue)
            {
                try
                {
                    using var hive = RegistryKey.OpenBaseKey(item.Location.StartsWith("S-1-") ? RegistryHive.Users : RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = hive.OpenSubKey(item.Location);
                    var value = key?.GetValue(item.ValueName ?? "");
                    data = value is byte[] bytes ? Encoding.Unicode.GetString(bytes) : value as string;
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
            resolver.Enrich(item, data);
            item.Selected = false;
        }
    }

    private static string[] Children(RegistryKey hive, string path)
    {
        try { using var key = hive.OpenSubKey(path); return key?.GetSubKeyNames() ?? []; }
        catch (System.Security.SecurityException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
