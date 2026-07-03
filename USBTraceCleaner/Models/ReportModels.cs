namespace USBTraceCleaner.Models;

public enum ReportModule
{
    Usb,
    Network
}

public enum ReportOperationType
{
    Scan,
    Clean,
    GhostClean
}

public sealed class ReportSummary
{
    public int TotalItems { get; init; }
    public int SelectedItems { get; init; }
    public int Processed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int? UsbStorageCount { get; init; }
    public int? UsbStorRemaining { get; init; }
    public int? AllowedCount { get; init; }
    public int? UnknownCount { get; init; }
    public int? CleanableCount { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = [];
    public IReadOnlyList<string> SkippedItems { get; init; } = [];
    public IReadOnlyList<(string Label, int Count)> CategoryCounts { get; init; } = [];
    public string? PeriodText { get; init; }
    public string? OptionsText { get; init; }
    public IReadOnlyList<NetworkAuditSummarySection> ConnectionSummary { get; init; } = [];
}

public sealed class UsbReportRow
{
    public bool Selected { get; init; }
    public string Group { get; init; } = "";
    public string Type { get; init; } = "";
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class NetworkReportRow
{
    public bool Selected { get; init; }
    public string Authorization { get; init; } = "";
    public string Time { get; init; } = "";
    public string Group { get; init; } = "";
    public string Action { get; init; } = "";
    public string Title { get; init; } = "";
    public string Effect { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed class PdfReportRequest
{
    public ReportModule Module { get; init; }
    public ReportOperationType Operation { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public required string ComputerName { get; init; }
    public required string OsVersion { get; init; }
    public required string AppVersion { get; init; }
    public required string LogText { get; init; }
    public ReportSummary Summary { get; init; } = new();
    public IReadOnlyList<UsbReportRow> UsbItems { get; init; } = [];
    public IReadOnlyList<NetworkReportRow> NetworkItems { get; init; } = [];
}
