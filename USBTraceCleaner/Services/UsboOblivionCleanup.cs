using Microsoft.Win32;

using USBTraceCleaner.Models;



namespace USBTraceCleaner.Services;



/// <summary>

/// Сбор и удаление ключей реестра по алгоритму USBOblivion (без полного reg query /s).

/// </summary>

public static class UsboOblivionCleanup

{

    private static readonly string[] StorageServices = ["USBSTOR", "UASPStor"];



    public static int CleanControlSet(string controlSet, CleanupOptions options, Action<string>? log = null)

    {

        var paths = CollectPaths(controlSet, options);

        if (paths.Count == 0)

        {

            log?.Invoke($"  [{controlSet}] следов USB-накопителей не найдено");

            return 0;

        }



        log?.Invoke($"  [{controlSet}] ключей к удалению: {paths.Count}");

        RegistrySecurityHelper.EnsureDeletePrivileges();



        var failed = new List<string>();

        foreach (var path in paths.OrderByDescending(p => p.Length))

        {

            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))

                continue;



            PrepareKeyForDelete(path);

            if (RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, path))

            {

                log?.Invoke($"[OK]  KEY HKLM\\{path}");

                continue;

            }



            if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))

                failed.Add(path);

        }



        if (failed.Count > 0)
        {
            log?.Invoke($"  [{controlSet}] SYSTEM reg delete ({failed.Count})...");
            var systemPaths = failed.SelectMany(p => ExpandDeletePaths(p)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => $"HKLM\\{p}").ToList();
            RegistrySystemHelper.TryBatchRegDeleteAsSystem(systemPaths);
        }



        var remaining = 0;

        foreach (var path in paths.OrderByDescending(p => p.Length))

        {

            if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))

                continue;



            remaining++;

            if (RegistryHelper.DeleteKey(RegistryHive.LocalMachine, path, false, log))

                remaining--;

        }



        return remaining;

    }



    public static int CleanMountedDevices(CleanupOptions options, Action<string>? log = null)

    {

        if (options.SimulationMode) return 0;



        const string path = @"SYSTEM\MountedDevices";

        var driveMask = RegistryHelper.GetConnectedDriveMask();

        var values = new List<string>();



        RegistryHelper.SafeOpen(mdKey =>

        {

            foreach (var valueName in mdKey.GetValueNames())

            {

                var data = mdKey.GetValue(valueName) as byte[];

                if (data == null) continue;



                var text = System.Text.Encoding.Unicode.GetString(data);

                if (!text.Contains("USBSTOR#Disk", StringComparison.OrdinalIgnoreCase)

                    && !text.Contains("USBSTOR#CdRom", StringComparison.OrdinalIgnoreCase)

                    && !text.Contains("STORAGE#RemovableMedia", StringComparison.OrdinalIgnoreCase))

                    continue;



                if (valueName.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase)

                    && valueName.Length >= 13)

                {

                    var letter = valueName[12];

                    if (RegistryHelper.IsDriveConnected(driveMask, letter))

                        continue;

                }



                values.Add(valueName);

            }

        }, RegistryHive.LocalMachine, path);



        if (values.Count == 0) return 0;



        log?.Invoke($"  MountedDevices: значений к удалению: {values.Count}");

        var failed = 0;

        foreach (var valueName in values)

        {

            if (!RegistryHelper.DeleteValue(RegistryHive.LocalMachine, path, valueName, false, log))

                failed++;

        }



        return failed;

    }



    public static HashSet<string> CollectPaths(string controlSet, CleanupOptions options)

    {

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var vidPidIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var prefix = $@"SYSTEM\{controlSet}";



        AddKey(paths, $@"{prefix}\Enum\USBSTOR");

        AddKey(paths, $@"{prefix}\Services\USBSTOR\Enum");

        AddKey(paths, $@"{prefix}\Enum\USBPRINT");

        DeviceMigrationCleaner.CollectStoragePaths(prefix, paths, options);

        CollectEnumUsbStorage(paths, vidPidIds, prefix, options);

        CollectUsbStorDriverRefs(paths, prefix);

        CollectUsbDeviceClassLinks(paths, prefix);

        CollectDeviceContainers(paths, prefix);

        CollectStaticDeviceClassKeys(paths, prefix);

        CollectUsbFlags(paths, vidPidIds, prefix);



        return paths;

    }



    private static void CollectEnumUsbStorage(

        HashSet<string> paths,

        HashSet<string> vidPidIds,

        string prefix,

        CleanupOptions options)

    {

        var usbEnum = $@"{prefix}\Enum\USB";

        foreach (var vid in ListSubKeys(usbEnum))

        {

            if (!vid.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) continue;



            var vidPath = $@"{usbEnum}\{vid}";

            var instancesToDelete = new List<string>();

            var instanceCount = 0;



            foreach (var instance in ListSubKeys(vidPath))

            {

                instanceCount++;

                var instancePath = $@"{vidPath}\{instance}";



                if (options.CleanAllUsbDevices || ShouldDeleteUsbInstance(instancePath, options))

                {

                    instancesToDelete.Add(instancePath);

                    vidPidIds.Add(vid);



                    var containerId = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "ContainerID");

                    if (!string.IsNullOrEmpty(containerId))

                        AddKey(paths, $@"{prefix}\Control\DeviceContainers\{containerId}");



                    var driver = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Driver");

                    if (!string.IsNullOrEmpty(driver))

                        AddKey(paths, $@"{prefix}\Control\Class\{driver}");

                }

            }



            foreach (var instancePath in instancesToDelete)

                AddKey(paths, instancePath);



            if (instanceCount > 0 && instancesToDelete.Count == instanceCount)

                AddKey(paths, vidPath);

        }

    }



    private static void CollectUsbStorDriverRefs(HashSet<string> paths, string prefix)

    {

        var usbStor = $@"{prefix}\Enum\USBSTOR";

        foreach (var deviceType in ListSubKeys(usbStor))

        {

            var typePath = $@"{usbStor}\{deviceType}";

            foreach (var instance in ListSubKeys(typePath))

            {

                var instancePath = $@"{typePath}\{instance}";

                var driver = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Driver");

                if (!string.IsNullOrEmpty(driver))

                    AddKey(paths, $@"{prefix}\Control\Class\{driver}");

            }

        }

    }



    private static void CollectUsbDeviceClassLinks(HashSet<string> paths, string prefix)

    {

        const string deviceClass = @"Control\DeviceClasses\{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

        var fullKey = $@"{prefix}\{deviceClass}";



        RegistryHelper.SafeOpen(classKey =>

        {

            foreach (var sub in classKey.GetSubKeyNames())

            {

                if (!sub.Contains("USB#Vid", StringComparison.OrdinalIgnoreCase)) continue;



                var deviceInstance = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, $@"{fullKey}\{sub}", "DeviceInstance");

                if (string.IsNullOrEmpty(deviceInstance)) continue;



                var enumKey = $@"{prefix}\Enum\{deviceInstance}";

                var service = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, enumKey, "Service");

                if (string.IsNullOrEmpty(service)) continue;



                if (service.Equals("USBSTOR", StringComparison.OrdinalIgnoreCase)

                    || service.Equals("UASPStor", StringComparison.OrdinalIgnoreCase))

                {

                    AddKey(paths, $@"{fullKey}\{sub}");

                    AddKey(paths, enumKey);

                }

            }

        }, RegistryHive.LocalMachine, fullKey);

    }



    private static void CollectDeviceContainers(HashSet<string> paths, string prefix)

    {

        var containersPath = $@"{prefix}\Control\DeviceContainers";

        RegistryHelper.SafeOpen(containersKey =>

        {

            foreach (var container in containersKey.GetSubKeyNames())

            {

                var containerPath = $@"{containersPath}\{container}";

                RegistryHelper.SafeOpen(baseKey =>

                {

                    foreach (var valueName in baseKey.GetValueNames())

                    {

                        var value = baseKey.GetValue(valueName)?.ToString() ?? string.Empty;

                        if (value.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)
                            || value.Contains("UASPStor", StringComparison.OrdinalIgnoreCase))

                        {

                            AddKey(paths, containerPath);

                            return;

                        }

                    }

                }, RegistryHive.LocalMachine, containerPath);

            }

        }, RegistryHive.LocalMachine, containersPath);

    }



    private static void CollectStaticDeviceClassKeys(HashSet<string> paths, string prefix)

    {

        foreach (var (relPath, filter, deleteAll) in OblivionDeviceClassPaths)

        {

            var full = $@"{prefix}\{relPath}";

            if (deleteAll)

            {

                AddKey(paths, full);

                continue;

            }



            RegistryHelper.SafeOpen(key =>

            {

                foreach (var sub in key.GetSubKeyNames())

                {

                    if (string.IsNullOrEmpty(filter) || sub.Contains(filter, StringComparison.OrdinalIgnoreCase))

                        AddKey(paths, $@"{full}\{sub}");

                }

            }, RegistryHive.LocalMachine, full);

        }

    }



    private static void CollectUsbFlags(HashSet<string> paths, HashSet<string> vidPidIds, string prefix)

    {

        var flagsPath = $@"{prefix}\Control\usbflags";

        RegistryHelper.SafeOpen(flagsKey =>

        {

            foreach (var flagName in flagsKey.GetSubKeyNames())

            {

                if (flagName.Length < 8) continue;

                var usbflagsId = $"vid_{flagName[..4]}&pid_{flagName[4..8]}";

                foreach (var vidPid in vidPidIds)

                {

                    if (vidPid.Equals(usbflagsId, StringComparison.OrdinalIgnoreCase))

                    {

                        AddKey(paths, $@"{flagsPath}\{flagName}");

                        break;

                    }

                }

            }

        }, RegistryHive.LocalMachine, flagsPath);

    }



    private static readonly (string Path, string Filter, bool DeleteAll)[] OblivionDeviceClassPaths =

    [

        (@"Control\DeviceClasses\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR#Disk", false),

        (@"Control\DeviceClasses\{53f56308-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR#CdRom", false),

        (@"Control\DeviceClasses\{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}", "USBSTOR", false),

        (@"Control\DeviceClasses\{6ac27878-a6fa-4155-ba85-f98f491d4f33}", "USBSTOR", false),

        (@"Control\DeviceClasses\{f33fdc04-d1ac-4e8e-9a30-19bbd4b108ae}", "USBSTOR", false),

        (@"Control\DeviceClasses\{10497b1b-ba51-44e5-8318-a65c837b6661}", "USBSTOR", false),

        (@"Control\DeviceClasses\{7fccc86c-228a-40ad-8a58-f590af7bfdce}", "USBSTOR", false),

        (@"Control\DeviceClasses\{7f108a28-9833-4b3b-b780-2c6b5fa5c062}", "USBSTOR", false),

        (@"Control\DeviceClasses\{6ead3d82-25ec-46bc-b7fd-c1f0df8f5037}", "USBSTOR", false),

        (@"Enum\SWD\WPDBUSENUM", "USBSTOR", false),

        (@"Enum\STORAGE\Volume", "USBSTOR", false),

    ];



    private static void PrepareKeyForDelete(string path)
    {
        RegistrySecurityHelper.EnsureDeletePrivileges();

        var propsPath = $@"{path}\Properties";
        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, propsPath))
        {
            RegistrySecurityHelper.TakeOwnershipTree(RegistryHive.LocalMachine, propsPath);
            if (!RegistryHelper.DeleteKey(RegistryHive.LocalMachine, propsPath, false, null))
            {
                RegistryNativeHelper.DeleteKeyTree(RegistryHive.LocalMachine, propsPath);
                if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, propsPath))
                    RegistrySystemHelper.TryRegDeleteAsSystem("HKLM", propsPath);
            }
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

    private static void AddKey(HashSet<string> paths, string path)

    {

        if (RegistryHelper.KeyExists(RegistryHive.LocalMachine, path))

            paths.Add(path);

    }



    private static bool ShouldDeleteUsbInstance(string instancePath, CleanupOptions options)

    {

        if (options.CleanAllUsbDevices) return true;



        var service = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Service");

        if (string.IsNullOrEmpty(service)) return false;



        if (StorageServices.Any(s => service.Equals(s, StringComparison.OrdinalIgnoreCase)))

            return true;



        if (options.CleanMtpDevices &&

            (service.Equals("WUDFWpdMtp", StringComparison.OrdinalIgnoreCase)

             || service.Equals("WUDFRd", StringComparison.OrdinalIgnoreCase)

             || service.Equals("WpdUpFltr", StringComparison.OrdinalIgnoreCase)))

            return true;



        return options.CleanKeyboardMouse

               && service.Equals("HidUsb", StringComparison.OrdinalIgnoreCase);

    }



    private static List<string> ListSubKeys(string path)

    {

        var result = new List<string>();

        RegistryHelper.SafeOpen(key =>

        {

            foreach (var name in key.GetSubKeyNames())

                result.Add(name);

        }, RegistryHive.LocalMachine, path);

        return result;

    }

}


