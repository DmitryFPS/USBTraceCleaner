using System.Text;
using System.Text.RegularExpressions;

namespace USBTraceCleaner.Services;

/// <summary>Общие эвристики USB / self-trace для сканеров (покрываются тестами).</summary>
public static class ForensicTracePatterns
{
    private static readonly Regex UsbStorToken = new(
        @"USBSTOR|WPDBUSENUM|UASPStor|RemovableMedia|STORAGE#Removable",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SelfExeNames =
    [
        "USBTraceCleaner",
        "USBOblivion",
        "USBOblivion64",
        "USBDeview",
        "USBDetector",
        "UsbForensicAudit",
    ];

    public static IReadOnlyList<string> SelfExecutableNames => SelfExeNames;

    private static readonly string[] RemovableUsbServices =
    [
        "USBSTOR",
        "UASPStor",
        "WUDFWpdMtp",
        "WUDFRd",
        "WpdUpFltr",
    ];

    public static bool ContainsUsbStorageToken(string? text) =>
        !string.IsNullOrEmpty(text) && UsbStorToken.IsMatch(text);

    /// <summary>
    /// Данные значения HKLM\SYSTEM\MountedDevices относятся к USB/WPD-накопителю.
    /// Раньше ловили только USBSTOR#Disk — из‑за этого оставались H: / Volume{…} от WPD.
    /// </summary>
    public static bool IsMountedDevicesUsbData(byte[]? data)
    {
        if (data is null || data.Length == 0) return false;

        var utf16 = Encoding.Unicode.GetString(data).TrimEnd('\0');
        if (IsMountedDevicesUsbPath(utf16)) return true;

        var ascii = Encoding.ASCII.GetString(data).TrimEnd('\0');
        return IsMountedDevicesUsbPath(ascii);
    }

    public static bool IsMountedDevicesUsbPath(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (ContainsUsbStorageToken(text)) return true;
        if (text.Contains("UDisk", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains(@"\USBSTOR", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("SWD#WPDBUSENUM", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("WPDBUSENUM", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("WpdMtp", StringComparison.OrdinalIgnoreCase)) return true;
        // Disk&Ven_ только вместе с USB-префиксом (не SCSI/NVMe)
        if (text.Contains("Disk&Ven_", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)
                || text.Contains("USB#", StringComparison.OrdinalIgnoreCase)
                || text.Contains("UASP", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    /// <summary>
    /// Отключённая буква D–Z в MountedDevices: кандидат на orphan-очистку
    /// (вместе с парным \??\Volume{guid} с теми же байтами).
    /// </summary>
    public static bool IsOrphanDosDeviceLetterName(string? valueName, int connectedDriveMask)
    {
        if (string.IsNullOrEmpty(valueName)) return false;
        if (!valueName.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase)) return false;
        if (valueName.Length < 13) return false;
        var letter = char.ToUpperInvariant(valueName[12]);
        if (letter is < 'D' or > 'Z') return false;
        return !RegistryHelper.IsDriveConnected(connectedDriveMask, letter);
    }

    public static bool ContainsSelfTraceToken(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var name in SelfExeNames)
        {
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsUsbOrSelfTrace(string? text) =>
        ContainsUsbStorageToken(text) || ContainsSelfTraceToken(text);

    /// <summary>
    /// Jump List / Recent binary blob относится к USB-накопителю или (опционально) self-trace.
    /// Не включает «все файлы папки» — только содержательные совпадения.
    /// </summary>
    public static bool IsJumpListContentOfInterest(string? text, int connectedDriveMask, bool includeSelfTraces)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (ContainsUsbStorageToken(text)) return true;
        if (MatchesRemovableDriveLetter(text, connectedDriveMask)) return true;
        if (text.Contains("UDisk", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase)) return true;
        if (includeSelfTraces && ContainsSelfTraceToken(text)) return true;
        return false;
    }

    /// <summary>Дочерний ключ Windows Portable Devices\Devices связан с USB/WPD-накопителем.</summary>
    public static bool IsWindowsPortableDeviceUsbChild(string? deviceKeyName) =>
        !string.IsNullOrEmpty(deviceKeyName) && (
            ContainsUsbStorageToken(deviceKeyName)
            || deviceKeyName.Contains("UDisk", StringComparison.OrdinalIgnoreCase)
            || deviceKeyName.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase)
            || deviceKeyName.Contains("WPDBUSENUM", StringComparison.OrdinalIgnoreCase));

    /// <summary>PnP Service относится к съёмному USB-накопителю / MTP, а не к onboard HID/UVC/BT.</summary>
    public static bool IsRemovableUsbService(string? service)
    {
        if (string.IsNullOrEmpty(service)) return false;
        foreach (var name in RemovableUsbServices)
        {
            if (service.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsPrefetchOfInterest(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        if (!fileName.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)) return false;
        return ContainsSelfTraceToken(fileName) || ContainsUsbStorageToken(fileName);
    }

    public static bool MatchesRemovableDriveLetter(string text, int connectedDriveMask)
    {
        for (var c = 'D'; c <= 'Z'; c++)
        {
            if (text.Contains($"{c}:\\", StringComparison.OrdinalIgnoreCase)
                && !RegistryHelper.IsDriveConnected(connectedDriveMask, c))
                return true;
        }
        return false;
    }

    public static bool JumpListFolderNameIsValid(string folderName) =>
        folderName.Equals("AutomaticDestinations", StringComparison.OrdinalIgnoreCase)
        || folderName.Equals("CustomDestinations", StringComparison.OrdinalIgnoreCase);

    /// <summary>ROT13 для UserAssist value names.</summary>
    public static string Rot13(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= 'a' and <= 'z')
                chars[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z')
                chars[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(chars);
    }

    public static bool TryParseUsbFlagsName(string name, out string vid, out string pid)
    {
        vid = pid = "";
        if (string.IsNullOrEmpty(name)) return false;

        if (name.StartsWith("IgnoreHWSerNum", StringComparison.OrdinalIgnoreCase)
            && name.Length >= "IgnoreHWSerNum".Length + 8)
        {
            var hex = name[^8..];
            if (IsHex8(hex))
            {
                vid = hex[..4].ToUpperInvariant();
                pid = hex[4..].ToUpperInvariant();
                return true;
            }
        }

        if (name.Length >= 8 && IsHex8(name[..8]))
        {
            vid = name[..4].ToUpperInvariant();
            pid = name[4..8].ToUpperInvariant();
            return true;
        }

        return false;
    }

    private static bool IsHex8(string s)
    {
        if (s.Length < 8) return false;
        for (var i = 0; i < 8; i++)
        {
            var c = s[i];
            var ok = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!ok) return false;
        }
        return true;
    }
}
