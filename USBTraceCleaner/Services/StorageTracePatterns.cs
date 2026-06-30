namespace USBTraceCleaner.Services;

/// <summary>Маркеры следов USB-накопителей в строках реестра и логов.</summary>
public static class StorageTracePatterns
{
    private static readonly string[] StorageMarkers =
    [
        "USBSTOR",
        "UASPStor",
        "USBPRINT",
        "STORAGE#RemovableMedia",
        "SWD\\WPDBUSENUM",
        "WPDBUSENUM",
    ];

    private static readonly string[] MtpMarkers =
    [
        "WUDFWpdMtp",
        "WpdUpFltr",
        "WUDFRd",
    ];

    public static bool MatchesStorage(string? text) =>
        !string.IsNullOrEmpty(text) &&
        StorageMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesMtp(string? text) =>
        !string.IsNullOrEmpty(text) &&
        MtpMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesDeviceMigrationEntry(string? text, bool includeMtp) =>
        MatchesStorage(text) || (includeMtp && MatchesMtp(text));
}
