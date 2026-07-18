using System.Windows;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Services.Report;

public static class ReportExportService
{
    public static bool TrySavePdf(PdfReportRequest request, Window owner)
    {
        var op = request.Operation switch
        {
            ReportOperationType.Scan => "Scan",
            ReportOperationType.Clean => "Clean",
            ReportOperationType.GhostClean => "GhostClean",
            _ => "Report"
        };

        var module = request.Module == ReportModule.Usb ? "USB" : "Network";
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить отчёт PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"USBTraceCleaner_{module}_{op}_{DateTime.Now:yyyy-MM-dd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true)
            return false;

        try
        {
            PdfReportGenerator.Generate(request, dialog.FileName);
            MessageBox.Show(
                $"Отчёт сохранён:\n{dialog.FileName}",
                "PDF готов",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                $"Не удалось создать PDF:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    public static string GetAppVersion() => AppInfo.Version;

    public static PdfReportRequest BuildUsbReport(
        ReportOperationType operation,
        IEnumerable<ArtifactItem> items,
        string logText,
        CleanupOptions? options,
        CleanupResult? cleanResult)
    {
        var list = items.ToList();
        var categoryCounts = list
            .GroupBy(i => i.DisplayViewGroup)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        var summary = new ReportSummary
        {
            TotalItems = list.Count,
            SelectedItems = list.Count(i => i.Selected),
            UsbStorageCount = list.Count(i => i.ViewGroup == ArtifactViewGroup.UsbStorage),
            CategoryCounts = categoryCounts,
            Processed = cleanResult?.ItemsProcessed ?? 0,
            Failed = cleanResult?.FailedCount ?? 0,
            UsbStorRemaining = cleanResult?.UsbStorRemaining,
            OptionsText = options == null ? null : FormatUsbOptions(options)
        };

        var rows = list.Select(i => new UsbReportRow
        {
            Selected = i.Selected,
            Group = i.DisplayViewGroup,
            Type = i.Type.ToString(),
            Location = i.Location,
            Description = i.Description ?? i.Detail ?? ""
        }).ToList();

        return new PdfReportRequest
        {
            Module = ReportModule.Usb,
            Operation = operation,
            ComputerName = Environment.MachineName,
            OsVersion = AdminHelper.GetWindowsVersionLabel(),
            AppVersion = GetAppVersion(),
            LogText = logText,
            Summary = summary,
            UsbItems = rows
        };
    }

    public static PdfReportRequest BuildNetworkReport(
        ReportOperationType operation,
        IEnumerable<NetworkAuditItem> items,
        string logText,
        NetworkAuditOptions? options,
        NetworkAuditCleaner.CleanResult? cleanResult)
    {
        var list = items.ToList();
        var categoryCounts = list
            .GroupBy(i => i.DisplayGroup)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, g.Count()))
            .ToList();

        var readableSummary = NetworkAuditSummaryBuilder.Build(list, options?.Whitelist);

        var summary = new ReportSummary
        {
            TotalItems = list.Count,
            SelectedItems = list.Count(i => i.Selected),
            AllowedCount = list.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Allowed),
            UnknownCount = list.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown),
            CleanableCount = list.Count(i => i.CanClean),
            CategoryCounts = categoryCounts,
            Processed = cleanResult?.Processed ?? 0,
            Failed = cleanResult?.Failed ?? 0,
            Skipped = cleanResult?.Skipped ?? 0,
            Failures = cleanResult?.Failures ?? [],
            SkippedItems = cleanResult?.SkippedItems ?? [],
            PeriodText = options == null
                ? null
                : $"{options.DateFrom:dd.MM.yyyy} — {options.DateTo:dd.MM.yyyy}",
            OptionsText = options == null ? null : FormatNetworkOptions(options),
            ConnectionSummary = readableSummary.Sections
        };

        var rows = list.Select(i => new NetworkReportRow
        {
            Selected = i.Selected,
            Authorization = i.DisplayAuthorization,
            Time = i.DisplayTime,
            Group = i.DisplayGroup,
            Action = NetworkAuditHints.GetActionLabel(i.CanClean),
            Title = i.Title,
            Effect = NetworkAuditHints.GetCleanEffect(i.Kind, i.Title),
            Detail = i.DisplayDetail
        }).ToList();

        return new PdfReportRequest
        {
            Module = ReportModule.Network,
            Operation = operation,
            ComputerName = Environment.MachineName,
            OsVersion = AdminHelper.GetWindowsVersionLabel(),
            AppVersion = GetAppVersion(),
            LogText = logText,
            Summary = summary,
            NetworkItems = rows
        };
    }

    private static string FormatUsbOptions(CleanupOptions o) =>
        string.Join(", ",
            new[]
            {
                o.SaveBackup ? "бэкап .reg" : null,
                o.CreateRestorePoint ? "точка восстановления" : null,
                o.CloseExplorer ? "закрыть Explorer" : null,
                o.RebootAfterClean ? "перезагрузка" : null,
                o.CleanMtpDevices ? "MTP" : null,
                o.ScrubLogFiles ? "чистка логов" : null,
                o.CleanShellBags ? "ShellBags" : null,
                o.CleanRecentLinks ? "Recent/JumpLists" : null,
                o.CleanBamEntries ? "BAM/DAM" : null,
                o.CleanExecutionArtifacts ? "Prefetch/Amcache/Shimcache" : null,
                o.CleanExplorerMru ? "Explorer MRU" : null,
                o.CleanRecycleBinUsb ? "Recycle Bin" : null,
                o.CleanVolumeShadowCopies ? "VSS" : null,
                o.CleanSelfTraces ? "self-trace" : null,
                o.CleanSystemEventLog ? "System log" : null,
                o.CleanAllUsbFlags ? "все usbflags" : null
            }.Where(s => s != null));

    private static string FormatNetworkOptions(NetworkAuditOptions o) =>
        string.Join(", ",
            new[]
            {
                o.FullCleanMode ? "макс. очистка" : null,
                o.DisconnectNetwork ? "отключить сеть" : null,
                o.RebootAfterClean ? "перезагрузка" : null,
                o.ScanWiFi ? "Wi‑Fi" : null,
                o.ScanEthernet ? "Ethernet" : null,
                o.ScanVpn ? "VPN" : null,
                o.ScanRouter ? "роутер" : null,
                o.ScanEventLogs ? "журналы" : null,
                o.ScanRegistry ? "реестр" : null,
                o.ScanCaches ? "кэши" : null
            }.Where(s => s != null));
}
