using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace USBTraceCleaner.Services;

/// <summary>
/// Удаление устройств через Configuration Manager (CfgMgr32) — корректный способ снятия Enum\USBSTOR.
/// </summary>
public static class DeviceUninstallHelper
{
    private const int CrSuccess = 0;
    private const int CmLocateDevnodeNormal = 0;
    private const int CmUninstallDefault = 0;
    private const int CmDisableUiNotOk = 0x00000001;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CM_Locate_DevNode(out uint pdnDevInst, string? pDeviceId, int ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Uninstall_DevNode(uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Disable_DevNode(uint dnDevInst, uint ulFlags);

    public static int UninstallUsbStorDevices(Action<string>? log = null)
    {
        var count = 0;
        var ids = CollectUsbStorInstanceIds();
        ids.AddRange(CollectEnumUsbStorageInstanceIds());

        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryUninstall(id, out var err))
            {
                log?.Invoke($"[OK]  DEV {id}");
                count++;
            }
            else
            {
                log?.Invoke($"[WARN] DEV {id} (ошибка {err})");
            }
        }

        return count;
    }

    public static int UninstallUsbDevices(IEnumerable<string> instanceIds, Action<string>? log = null)
    {
        var count = 0;
        foreach (var id in instanceIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryUninstall(id, out _))
            {
                log?.Invoke($"[OK]  DEV {id}");
                count++;
            }
        }
        return count;
    }

    public static bool IsDevicePresent(string deviceInstanceId) =>
        CM_Locate_DevNode(out _, deviceInstanceId, CmLocateDevnodeNormal) == CrSuccess;

    public static bool TryRemoveDevice(string deviceInstanceId)
    {
        if (TryUninstall(deviceInstanceId, out _))
            return true;

        // Запись в реестре без активного devnode — не ошибка
        return !IsDevicePresent(deviceInstanceId);
    }

    private static bool TryUninstall(string deviceInstanceId, out int errorCode)
    {
        errorCode = 0;
        var rc = CM_Locate_DevNode(out var devInst, deviceInstanceId, CmLocateDevnodeNormal);
        if (rc != CrSuccess)
        {
            errorCode = rc;
            // Призрачные записи в Enum\USBSTOR — устройства уже сняты, pnputil только зависает
            return false;
        }

        CM_Disable_DevNode(devInst, CmDisableUiNotOk);
        rc = CM_Uninstall_DevNode(devInst, CmUninstallDefault);
        if (rc == CrSuccess) return true;

        errorCode = rc;
        return TryPnPUtilRemove(deviceInstanceId);
    }

    private static bool TryPnPUtilRemove(string deviceInstanceId)
    {
        var psi = new ProcessStartInfo("pnputil.exe", $"/remove-device \"{deviceInstanceId}\" /force")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return false;
        if (!proc.WaitForExit(15000))
        {
            try { proc.Kill(true); } catch { }
            return false;
        }
        return proc.ExitCode == 0;
    }

    private static List<string> CollectUsbStorInstanceIds()
    {
        var ids = new List<string>();

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var path = $@"SYSTEM\{controlSet}\Enum\USBSTOR";
            RegistryHelper.SafeOpen(stor =>
            {
                foreach (var deviceType in stor.GetSubKeyNames())
                {
                    var typePath = $@"{path}\{deviceType}";
                    RegistryHelper.SafeOpen(typeKey =>
                    {
                        foreach (var serial in typeKey.GetSubKeyNames())
                        {
                            var id = $@"USBSTOR\{deviceType}\{serial}";
                            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                                ids.Add(id);
                        }
                    }, RegistryHive.LocalMachine, typePath);
                }
            }, RegistryHive.LocalMachine, path);
        }

        return ids;
    }

    private static List<string> CollectEnumUsbStorageInstanceIds()
    {
        var ids = new List<string>();
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "USBSTOR", "UASPStor" };

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var usbPath = $@"SYSTEM\{controlSet}\Enum\USB";
            RegistryHelper.SafeOpen(usb =>
            {
                foreach (var vid in usb.GetSubKeyNames())
                {
                    if (vid.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase)) continue;
                    var vidPath = $@"{usbPath}\{vid}";
                    RegistryHelper.SafeOpen(vidKey =>
                    {
                        foreach (var instance in vidKey.GetSubKeyNames())
                        {
                            var instancePath = $@"{vidPath}\{instance}";
                            var service = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Service");
                            if (string.IsNullOrEmpty(service) || !services.Contains(service))
                                continue;

                            var id = $@"USB\{vid}\{instance}";
                            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                                ids.Add(id);
                        }
                    }, RegistryHive.LocalMachine, vidPath);
                }
            }, RegistryHive.LocalMachine, usbPath);
        }

        return ids;
    }

    public static List<string> CollectUsbInstanceIds(bool allDevices)
    {
        var ids = new List<string>();

        foreach (var controlSet in RegistryHelper.EnumerateControlSets())
        {
            var usbPath = $@"SYSTEM\{controlSet}\Enum\USB";
            RegistryHelper.SafeOpen(usb =>
            {
                foreach (var vid in usb.GetSubKeyNames())
                {
                    if (vid.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase)) continue;
                    var vidPath = $@"{usbPath}\{vid}";
                    RegistryHelper.SafeOpen(vidKey =>
                    {
                        foreach (var instance in vidKey.GetSubKeyNames())
                        {
                            var id = $@"USB\{vid}\{instance}";
                            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                                ids.Add(id);
                        }
                    }, RegistryHive.LocalMachine, vidPath);
                }
            }, RegistryHive.LocalMachine, usbPath);
        }

        return ids;
    }
}
