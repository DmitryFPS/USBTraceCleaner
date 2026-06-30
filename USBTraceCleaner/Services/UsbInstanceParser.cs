namespace USBTraceCleaner.Services;

/// <summary>
/// Разбор instance ID в Enum\USB (например 6&amp;3ad2d465&amp;2&amp;0000).
/// </summary>
public static class UsbInstanceParser
{
    public static bool TryParseReEnumerationInstance(string instance, out string hubBase, out int enumIndex, out string interfaceSlot)
    {
        hubBase = string.Empty;
        enumIndex = 0;
        interfaceSlot = string.Empty;

        if (string.IsNullOrWhiteSpace(instance))
            return false;

        var parts = instance.Split('&');
        if (parts.Length < 4)
            return false;

        if (!int.TryParse(parts[2], out enumIndex))
            return false;

        hubBase = $"{parts[0]}&{parts[1]}";
        interfaceSlot = parts[3];
        return true;
    }

    public static string GetDuplicateGroupKey(string vidKey, string instance)
    {
        if (TryParseReEnumerationInstance(instance, out var hubBase, out _, out var slot))
            return $"{vidKey}|{hubBase}|{slot}";

        return $"{vidKey}|unique|{instance}";
    }

    public static int GetEnumIndex(string instance)
    {
        return TryParseReEnumerationInstance(instance, out _, out var index, out _) ? index : 0;
    }

    public static string ToDeviceInstanceId(string vidKey, string instance) => $@"USB\{vidKey}\{instance}";
}
