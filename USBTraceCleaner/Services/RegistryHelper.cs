using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace USBTraceCleaner.Services;

public static class AdminHelper
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsWindows10Or11()
    {
        var version = Environment.OSVersion.Version;
        return version.Major == 10 && version.Build >= 10240;
    }

    public static string GetWindowsVersionLabel()
    {
        var build = Environment.OSVersion.Version.Build;
        return build >= 22000 ? "Windows 11" : "Windows 10";
    }
}

public static class RegistryHelper
{
    private const int KEY_WOW64_64KEY = 0x0100;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteKeyEx(IntPtr hKey, string lpSubKey, int samDesired, int reserved);

    public static IEnumerable<string> EnumerateControlSets()
    {
        var result = new List<string>();
        try
        {
            using var system = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SYSTEM");
            if (system == null) return result;

            foreach (var name in system.GetSubKeyNames())
            {
                if (name.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase) &&
                    name.Length == 13)
                {
                    result.Add(name);
                }
            }
        }
        catch { /* ignore */ }

        return result;
    }

    public static void SafeOpen(Action<RegistryKey> action, RegistryHive hive, string subKey, bool writable = false)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable);
            if (key != null) action(key);
        }
        catch (SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static bool KeyExists(RegistryHive hive, string subKey)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetStringValueAt(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch { return null; }
    }

    /// <summary>Экземпляры Enum\USB + USBSTOR в активном ControlSet (все USB, не только накопители).</summary>
    public static int CountUsbDeviewDevices()
    {
        var current = GetCurrentControlSetName();
        if (current == null) return CountUsbStorDevices();

        var total = CountUsbStorInstances(current);
        var usbPath = $@"SYSTEM\{current}\Enum\USB";
        SafeOpen(usb =>
        {
            foreach (var vid in usb.GetSubKeyNames())
            {
                if (vid.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase)) continue;
                SafeOpen(vidKey =>
                {
                    total += vidKey.SubKeyCount;
                }, RegistryHive.LocalMachine, $@"{usbPath}\{vid}");
            }
        }, RegistryHive.LocalMachine, usbPath);
        return total;
    }

    /// <summary>Следы USB-накопителей: USBSTOR + UASPStor (+ MTP при необходимости).</summary>
    public static int CountUsbStorageTraceDevices(bool includeMtp = true)
    {
        var current = GetCurrentControlSetName();
        if (current == null) return CountUsbStorDevices();

        var total = CountUsbStorInstances(current);
        total += CountEnumUsbByService(current, StorageServices, StringComparison.OrdinalIgnoreCase);

        if (includeMtp)
            total += CountEnumUsbByService(current, MtpServices, StringComparison.OrdinalIgnoreCase);

        return total;
    }

    private static readonly string[] StorageServices = ["USBSTOR", "UASPStor"];
    private static readonly string[] MtpServices = ["WUDFWpdMtp", "WUDFRd", "WpdUpFltr"];

    private static int CountUsbStorInstances(string controlSet)
    {
        var count = 0;
        var path = $@"SYSTEM\{controlSet}\Enum\USBSTOR";
        SafeOpen(stor =>
        {
            foreach (var deviceType in stor.GetSubKeyNames())
            {
                SafeOpen(typeKey =>
                {
                    count += typeKey.SubKeyCount;
                }, RegistryHive.LocalMachine, $@"{path}\{deviceType}");
            }
        }, RegistryHive.LocalMachine, path);
        return count;
    }

    private static int CountEnumUsbByService(string controlSet, string[] services, StringComparison comparison)
    {
        var count = 0;
        var usbPath = $@"SYSTEM\{controlSet}\Enum\USB";
        SafeOpen(usb =>
        {
            foreach (var vid in usb.GetSubKeyNames())
            {
                if (vid.StartsWith("ROOT_", StringComparison.OrdinalIgnoreCase)) continue;
                var vidPath = $@"{usbPath}\{vid}";
                SafeOpen(vidKey =>
                {
                    foreach (var instance in vidKey.GetSubKeyNames())
                    {
                        var service = GetStringValueAt(RegistryHive.LocalMachine, $@"{vidPath}\{instance}", "Service");
                        if (!string.IsNullOrEmpty(service)
                            && services.Any(s => service.Equals(s, comparison)))
                            count++;
                    }
                }, RegistryHive.LocalMachine, vidPath);
            }
        }, RegistryHive.LocalMachine, usbPath);
        return count;
    }

    public static string? GetStringValue(RegistryKey parent, string subKeyName, string valueName)
    {
        using var sub = parent.OpenSubKey(subKeyName);
        return sub?.GetValue(valueName)?.ToString();
    }

    public static bool DeleteKey(RegistryHive hive, string subKey, bool simulation, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] KEY {GetHivePrefix(hive)}\\{subKey}");
            return true;
        }

        if (!KeyExists(hive, subKey))
            return true;

        if (subKey.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase))
            RegistrySecurityHelper.TakeOwnershipPath(hive, subKey);

        var prefix = GetHivePrefix(hive);
        var fullPath = $"{prefix}\\{subKey}";

        // 1) reg.exe /f (надёжен для одиночных ключей)
        if (TryRegDelete(fullPath))
        {
            log?.Invoke($"[OK]  KEY {fullPath} (reg.exe)");
            return true;
        }

        // 2) PowerShell Remove-Item
        if (TryPowerShellDelete(hive, subKey))
        {
            log?.Invoke($"[OK]  KEY {fullPath} (PowerShell)");
            return true;
        }

        // 3) Рекурсивное RegDeleteKeyEx
        if (DeleteKeyRecursiveNative(hive, subKey) == 0 || !KeyExists(hive, subKey))
        {
            log?.Invoke($"[OK]  KEY {fullPath} (native)");
            return true;
        }

        // 4) reg delete от SYSTEM — только для одиночных ключей, не для массовой очистки артефактов
        if (subKey.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase)
            && subKey.Contains(@"\Enum\USBSTOR\", StringComparison.OrdinalIgnoreCase)
            && RegistrySystemHelper.TryRegDeleteAsSystem(prefix, subKey)
            && !KeyExists(hive, subKey))
        {
            log?.Invoke($"[OK]  KEY {fullPath} (SYSTEM)");
            return true;
        }

        if (!KeyExists(hive, subKey))
        {
            log?.Invoke($"[OK]  KEY {fullPath} (уже удалён)");
            return true;
        }

        log?.Invoke($"[FAIL] KEY {fullPath}");
        return false;
    }

    public static void DeleteKeyTree(RegistryHive hive, string subKey, bool simulation, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] TREE {GetHivePrefix(hive)}\\{subKey}");
            return;
        }

        SafeOpen(parent =>
        {
            foreach (var child in parent.GetSubKeyNames().ToArray())
            {
                var childPath = string.IsNullOrEmpty(subKey) ? child : $"{subKey}\\{child}";
                if (HasSubKeys(hive, childPath))
                    DeleteKeyTree(hive, childPath, simulation, log);
                else
                    DeleteKey(hive, childPath, simulation, log);
            }
        }, hive, subKey);

        DeleteKey(hive, subKey, simulation, log);
    }

    private static bool HasSubKeys(RegistryHive hive, string subKey)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.SubKeyCount > 0;
        }
        catch { return false; }
    }

    private static bool TryRegDelete(string fullPath)
    {
        return RunProcess("reg.exe", ["delete", fullPath, "/f"]) == 0;
    }

    private static bool TryPowerShellDelete(RegistryHive hive, string subKey)
    {
        var psPath = hive switch
        {
            RegistryHive.LocalMachine => $"HKLM:\\{subKey}",
            RegistryHive.CurrentUser => $"HKCU:\\{subKey}",
            RegistryHive.Users => $"HKU:\\{subKey}",
            _ => $"HKLM:\\{subKey}"
        };
        var script = $"Remove-Item -LiteralPath '{psPath.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue";
        return RunProcess("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]) == 0
               && !KeyExists(hive, subKey);
    }

    private static int RunProcess(string file, string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi);
        if (proc == null) return -1;
        if (!proc.WaitForExit(15000))
        {
            try { proc.Kill(true); } catch { }
            return -1;
        }
        return proc.ExitCode;
    }

    public static bool DeleteValue(RegistryHive hive, string subKey, string valueName, bool simulation, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] VAL {GetHivePrefix(hive)}\\{subKey}\\{valueName}");
            return true;
        }

        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, writable: true);
            if (key?.GetValue(valueName) != null)
                key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch { /* fallback */ }

        var code = RunReg($"delete \"{GetHivePrefix(hive)}\\{subKey}\" /v \"{valueName}\" /f");
        if (code == 0 || !ValueExists(hive, subKey, valueName))
        {
            log?.Invoke($"[OK]  VAL {GetHivePrefix(hive)}\\{subKey}\\{valueName}");
            return true;
        }
        log?.Invoke($"[FAIL] VAL {GetHivePrefix(hive)}\\{subKey}\\{valueName} (код {code})");
        return false;
    }

    /// <summary>
    /// Рекурсивное удаление ключа (алгоритм USBOblivion RegDeleteKeyFull).
    /// </summary>
    private static int DeleteKeyRecursiveNative(RegistryHive hive, string subKey)
    {
        var hivePtr = GetHiveHandle(hive);
        if (hivePtr == IntPtr.Zero) return -1;

        string[] children;
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            if (key == null) return 0;
            children = key.GetSubKeyNames();
        }
        catch
        {
            return -1;
        }

        foreach (var child in children)
        {
            var childPath = string.IsNullOrEmpty(subKey) ? child : $"{subKey}\\{child}";
            var rc = DeleteKeyRecursiveNative(hive, childPath);
            if (rc != 0 && SafeKeyExists(hive, childPath))
                return rc;
        }

        var result = RegDeleteKeyEx(hivePtr, subKey, KEY_WOW64_64KEY, 0);
        if (result != 0)
            result = RegDeleteKeyEx(hivePtr, subKey, 0, 0);
        return result;
    }

    private static bool SafeKeyExists(RegistryHive hive, string subKey)
    {
        try { return KeyExists(hive, subKey); }
        catch { return false; }
    }

    private static IntPtr GetHiveHandle(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => new IntPtr(unchecked((int)0x80000002)),
        RegistryHive.CurrentUser => new IntPtr(unchecked((int)0x80000001)),
        RegistryHive.Users => new IntPtr(unchecked((int)0x80000003)),
        _ => IntPtr.Zero
    };

    private static string GetHivePrefix(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.Users => "HKU",
        _ => "HKLM"
    };

    public static void ExportKey(string hivePrefix, string subKey, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        RunReg($"export \"{hivePrefix}\\{subKey}\" \"{filePath}\" /y");
    }

    public static void MergeRegExports(IEnumerable<(string HivePrefix, string SubKey)> exports, string outputFile)
    {
        const string header = "Windows Registry Editor Version 5.00\r\n\r\n";
        using var writer = new StreamWriter(outputFile, false, Encoding.Unicode);
        writer.Write(header);

        var tempDir = Path.Combine(Path.GetTempPath(), "USBTraceCleaner_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var index = 0;
            foreach (var (hivePrefix, subKey) in exports)
            {
                var tempFile = Path.Combine(tempDir, $"part_{index++}.reg");
                ExportKey(hivePrefix, subKey, tempFile);

                if (!File.Exists(tempFile)) continue;

                var content = File.ReadAllText(tempFile, Encoding.Unicode);
                var bodyStart = content.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (bodyStart >= 0)
                    writer.Write(content[(bodyStart + 4)..]);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    public static int RunReg(string arguments)
    {
        var psi = new ProcessStartInfo("reg.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return -1;
        proc.WaitForExit(120000);
        return proc.ExitCode;
    }

    public static IEnumerable<string> EnumerateUserSids()
    {
        using var users = Registry.Users;
        foreach (var sid in users.GetSubKeyNames())
        {
            if (sid.Contains("_Classes", StringComparison.OrdinalIgnoreCase)) continue;
            if (sid.Equals(".DEFAULT", StringComparison.OrdinalIgnoreCase)) continue;
            if (sid.StartsWith("S-1-5-21", StringComparison.OrdinalIgnoreCase))
                yield return sid;
        }
    }

    public static int GetConnectedDriveMask()
    {
        var mask = 0;
        foreach (var drive in Environment.GetLogicalDrives())
        {
            if (drive.Length < 1) continue;
            var letter = char.ToUpperInvariant(drive[0]);
            if (letter is >= 'A' and <= 'Z')
                mask |= 1 << (letter - 'A');
        }
        return mask;
    }

    public static bool IsDriveConnected(int driveMask, char letter)
    {
        var upper = char.ToUpperInvariant(letter);
        if (upper is < 'A' or > 'Z') return false;
        return (driveMask & (1 << (upper - 'A'))) != 0;
    }

    public static int CountSubKeys(RegistryHive hive, string subKey)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.SubKeyCount ?? 0;
        }
        catch { return -1; }
    }

    public static bool ValueExists(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) != null;
        }
        catch { return false; }
    }

    public static string? GetCurrentControlSetName()
    {
        try
        {
            using var select = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SYSTEM\Select");
            var current = select?.GetValue("Current");
            if (current is int n && n > 0)
                return $"ControlSet{n:000}";
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>
    /// Количество устройств USBSTOR в активном ControlSet (как видит USBDeview).
    /// </summary>
    public static int CountUsbStorDevices()
    {
        var current = GetCurrentControlSetName();
        if (current != null)
        {
            var count = CountSubKeys(RegistryHive.LocalMachine, $@"SYSTEM\{current}\Enum\USBSTOR");
            return count > 0 ? count : 0;
        }

        var total = 0;
        foreach (var cs in EnumerateControlSets())
        {
            var count = CountSubKeys(RegistryHive.LocalMachine, $@"SYSTEM\{cs}\Enum\USBSTOR");
            if (count > 0) total += count;
        }
        return total;
    }

    /// <summary>Все ControlSet, включая LastKnownGood (для полной очистки).</summary>
    public static int CountUsbStorDevicesAllControlSets()
    {
        var total = 0;
        foreach (var cs in EnumerateControlSets())
        {
            var count = CountSubKeys(RegistryHive.LocalMachine, $@"SYSTEM\{cs}\Enum\USBSTOR");
            if (count > 0) total += count;
        }
        return total;
    }

    public static bool HasUsbStorCdRom()
    {
        foreach (var cs in EnumerateControlSets())
        {
            var path = $@"SYSTEM\{cs}\Enum\USBSTOR";
            var found = false;
            SafeOpen(key =>
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    if (name.StartsWith("CdRom", StringComparison.OrdinalIgnoreCase))
                        found = true;
                }
            }, RegistryHive.LocalMachine, path);
            if (found) return true;
        }
        return false;
    }
}
