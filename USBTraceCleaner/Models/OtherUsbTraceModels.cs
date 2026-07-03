namespace USBTraceCleaner.Models;

public enum OtherUsbTraceSource
{
    UsbFlags,
    EnumUsb,
    DeviceMigration,
    SetupUpgradeMigration,
    Other
}

public sealed class OtherUsbTraceItem
{
    public bool Selected { get; set; } = true;
    public required string Vid { get; init; }
    public required string Pid { get; init; }
    public string Manufacturer { get; init; } = "—";
    public string Model { get; init; } = "—";
    public DateTime? FirstConnected { get; init; }
    public OtherUsbTraceSource SourceKind { get; init; }
    public string SourceLabel { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public IReadOnlyList<string> RegistryPaths { get; init; } = [];
    public IReadOnlyList<string> LogFilePaths { get; init; } = [];
    public string Detail { get; init; } = "";

    public string DisplayVid => Vid.ToUpperInvariant();
    public string DisplayPid => Pid.ToUpperInvariant();
    public string DisplayFirstConnected => FirstConnected?.ToString("dd.MM.yyyy HH:mm") ?? "—";
    public string DisplayLocation
    {
        get
        {
            if (RegistryPaths.Count > 0) return RegistryPaths[0];
            if (LogFilePaths.Count > 0) return LogFilePaths[0];
            return "";
        }
    }
}

public sealed class OtherUsbTraceCleanResult
{
    public bool Success { get; init; }
    public int Processed { get; init; }
    public int Failed { get; init; }
    public string Log { get; init; } = "";
    public string? ErrorMessage { get; init; }
}
