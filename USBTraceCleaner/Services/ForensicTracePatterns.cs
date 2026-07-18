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

    public static bool ContainsUsbStorageToken(string? text) =>
        !string.IsNullOrEmpty(text) && UsbStorToken.IsMatch(text);

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
