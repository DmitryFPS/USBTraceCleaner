using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace USBTraceCleaner.Services;

/// <summary>
/// Захват владения ключами реестра (TrustedInstaller/SYSTEM) перед удалением.
/// </summary>
public static class RegistrySecurityHelper
{
    private const int TokenAdjustPrivileges = 0x0020;
    private const int TokenQuery = 0x0008;
    private const int SePrivilegeEnabled = 0x00000002;
    private const int SeRegistryKey = 4;
    private const int OwnerSecurityInformation = 0x00000001;
    private const int DaclSecurityInformation = 0x00000004;
    private const uint SdRevision = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public int PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out Luid lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TokenPrivileges NewState,
        int BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SetNamedSecurityInfo(
        string pObjectName,
        int ObjectType,
        int SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string StringSecurityDescriptor,
        uint StringSDRevision,
        out IntPtr SecurityDescriptor,
        out IntPtr SecurityDescriptorSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static bool _privilegesEnabled;
    private static readonly SecurityIdentifier AdminSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static void EnsureDeletePrivileges()
    {
        if (_privilegesEnabled) return;

        EnablePrivilege("SeTakeOwnershipPrivilege");
        EnablePrivilege("SeRestorePrivilege");
        EnablePrivilege("SeBackupPrivilege");
        EnablePrivilege("SeSecurityPrivilege");
        _privilegesEnabled = true;
    }

    /// <summary>
    /// Захват владения ключом и всеми родительскими ключами в пути.
    /// </summary>
    public static void TakeOwnershipPath(RegistryHive hive, string subKey)
    {
        if (string.IsNullOrEmpty(subKey)) return;

        EnsureDeletePrivileges();

        var parts = subKey.Split('\\');
        // Не трогаем SYSTEM / ControlSet / Enum — только USBSTOR и ниже
        var start = 1;
        if (parts.Length >= 4
            && parts[0].Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("Enum", StringComparison.OrdinalIgnoreCase))
            start = 4;
        else if (parts.Length >= 1 && parts[0].Equals("SYSTEM", StringComparison.OrdinalIgnoreCase))
            start = Math.Min(4, parts.Length);

        for (var i = start; i <= parts.Length; i++)
        {
            var path = string.Join("\\", parts, 0, i);
            ApplyOwnership(hive, path);
        }
    }

    /// <summary>
    /// Захват владения ключом и всеми вложенными подключами (в т.ч. Properties).
    /// Обходит дерево через .NET после захвата каждого уровня — reg query /s не видит защищённые ключи.
    /// </summary>
    public static void TakeOwnershipTree(RegistryHive hive, string subKey)
    {
        TakeOwnershipPath(hive, subKey);
        TakeOwnershipChildren(hive, subKey);
    }

    private static void TakeOwnershipChildren(RegistryHive hive, string subKey)
    {
        foreach (var childName in ListSubKeyNames(hive, subKey))
        {
            var childPath = $"{subKey}\\{childName}";
            TakeOwnershipPath(hive, childPath);
            TakeOwnershipChildren(hive, childPath);
        }
    }

    private static IEnumerable<string> ListSubKeyNames(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ReadKey | RegistryRights.EnumerateSubKeys);
            if (key != null)
                return key.GetSubKeyNames();
        }
        catch { /* пробуем reg.exe */ }

        return RegistryQueryHelper.EnumerateImmediateSubKeyPaths(hive, subKey);
    }

    private static void ApplyOwnership(RegistryHive hive, string subKey)
    {
        EnsureDeletePrivileges();

        var objectName = ToSecurityObjectName(hive, subKey);
        var sidPtr = SidToPtr(AdminSid);

        try
        {
            SetNamedSecurityInfo(
                objectName,
                SeRegistryKey,
                OwnerSecurityInformation,
                sidPtr,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(sidPtr);
        }

        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.TakeOwnership
                    | RegistryRights.ChangePermissions
                    | RegistryRights.Delete
                    | RegistryRights.ReadKey
                    | RegistryRights.WriteKey);
            if (key == null) return;

            var acl = key.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            acl.SetOwner(AdminSid);
            acl.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            acl.AddAccessRule(new RegistryAccessRule(
                AdminSid,
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            key.SetAccessControl(acl);
            key.SetAccessControl(acl); // второй вызов — обход задержки применения DACL
        }
        catch
        {
            // SetNamedSecurityInfo уже мог достаточно изменить ACL
        }
    }

    private static IntPtr SidToPtr(SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    private static string ToSecurityObjectName(RegistryHive hive, string subKey) => hive switch
    {
        RegistryHive.LocalMachine => $"MACHINE\\{subKey}",
        RegistryHive.CurrentUser => $"CURRENT_USER\\{subKey}",
        RegistryHive.Users => $"USERS\\{subKey}",
        _ => $"MACHINE\\{subKey}"
    };

    private static void EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            return;

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
                return;

            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled }
            };

            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
