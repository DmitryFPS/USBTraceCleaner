using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class RegistryNativeHelper
{
    private const int KeyRead = 0x20019;
    private const int KeyWrite = 0x20006;
    private const int KeyEnumSubKey = 0x0008;
    private const int KeyDelete = 0x00010000;
    private const int KeyWow64_64Key = 0x0100;
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorAccessDenied = 5;

    public static int LastErrorCode { get; private set; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegEnumKeyEx(
        IntPtr hKey,
        int dwIndex,
        StringBuilder lpName,
        ref int lpcchName,
        IntPtr lpReserved,
        IntPtr lpClass,
        IntPtr lpcchClass,
        IntPtr lpftLastWriteTime);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteKeyEx(IntPtr hKey, string lpSubKey, int samDesired, int reserved);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteTree(IntPtr hKey, string lpSubKey);

    /// <summary>
    /// Удаляет дерево ключей: сначала RegDeleteTree (все потомки), затем сам ключ.
    /// </summary>
    public static bool DeleteKeyTree(RegistryHive hive, string subKey)
    {
        if (string.IsNullOrEmpty(subKey)) return true;

        var root = GetHiveHandle(hive);
        if (root == IntPtr.Zero) return false;

        var rc = RegDeleteTree(root, subKey);
        LastErrorCode = rc;
        if (rc == ErrorAccessDenied)
        {
            RegistrySecurityHelper.TakeOwnershipTree(hive, subKey);
            rc = RegDeleteTree(root, subKey);
            LastErrorCode = rc;
        }

        if (rc != ErrorSuccess && rc != ErrorFileNotFound && rc != ErrorAccessDenied)
            DeleteKeyRecurse(root, subKey);
        else if (rc == ErrorAccessDenied)
            DeleteKeyRecurse(root, subKey);

        rc = RegDeleteKeyEx(root, subKey, KeyWow64_64Key, 0);
        if (rc != ErrorSuccess && rc != ErrorFileNotFound)
            rc = RegDeleteKeyEx(root, subKey, 0, 0);
        LastErrorCode = rc;

        return rc == ErrorSuccess || rc == ErrorFileNotFound || !RegistryHelper.KeyExists(hive, subKey);
    }

    private static int DeleteKeyRecurse(IntPtr hiveRoot, string subKey)
    {
        var sam = KeyRead | KeyWrite | KeyEnumSubKey | KeyDelete | KeyWow64_64Key;
        var rc = RegOpenKeyEx(hiveRoot, subKey, 0, sam, out var hKey);
        if (rc == ErrorSuccess)
        {
            var deleted = 0;
            while (deleted++ < 256)
            {
                var name = new StringBuilder(512);
                var len = name.Capacity;
                rc = RegEnumKeyEx(hKey, 0, name, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (rc != ErrorSuccess) break;

                var childPath = string.IsNullOrEmpty(subKey) ? name.ToString() : $"{subKey}\\{name}";
                rc = DeleteKeyRecurse(hiveRoot, childPath);
                if (rc != ErrorSuccess && RegistryHelper.KeyExists(RegistryHive.LocalMachine, childPath))
                    break;
            }
            RegCloseKey(hKey);
        }
        else if (rc != ErrorFileNotFound)
        {
            return rc;
        }

        rc = RegDeleteKeyEx(hiveRoot, subKey, KeyWow64_64Key, 0);
        if (rc != ErrorSuccess)
            rc = RegDeleteKeyEx(hiveRoot, subKey, 0, 0);
        return rc;
    }

    private static IntPtr GetHiveHandle(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => new IntPtr(unchecked((int)0x80000002)),
        RegistryHive.CurrentUser => new IntPtr(unchecked((int)0x80000001)),
        RegistryHive.Users => new IntPtr(unchecked((int)0x80000003)),
        _ => IntPtr.Zero
    };
}
