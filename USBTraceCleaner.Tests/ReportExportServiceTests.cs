using System.IO;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.Report;

namespace USBTraceCleaner.Tests;

public class ReportExportServiceTests
{
    [Fact]
    public void BuildUsbReport_FillsSummaryAndRows()
    {
        var items = new[]
        {
            new ArtifactItem
            {
                Category = ArtifactCategory.RegistrySystem,
                Type = ArtifactType.RegistryKey,
                Location = @"SYSTEM\ControlSet001\Enum\USBSTOR\Disk",
                Description = "test",
                Selected = true
            }
        };

        var request = ReportExportService.BuildUsbReport(
            ReportOperationType.Scan, items, "log", new CleanupOptions { SaveBackup = true }, null);

        Assert.Equal(ReportModule.Usb, request.Module);
        Assert.Equal(1, request.Summary.TotalItems);
        Assert.Single(request.UsbItems);
        Assert.Contains("бэкап", request.Summary.OptionsText);
    }

    [Fact]
    public void BuildNetworkReport_IncludesReadableSummary()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "doZOR",
                Location = "doZOR",
                CanClean = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Allowed
            }
        };

        var request = ReportExportService.BuildNetworkReport(
            ReportOperationType.Scan, items, "log", new NetworkAuditOptions(), null);

        Assert.Equal(ReportModule.Network, request.Module);
        Assert.NotEmpty(request.Summary.ConnectionSummary);
        Assert.Single(request.NetworkItems);
    }

    [Fact]
    public void PdfReportGenerator_WritesUsbReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"utc_usb_{Guid.NewGuid():N}.pdf");
        try
        {
            var request = ReportExportService.BuildUsbReport(
                ReportOperationType.Scan,
                [new ArtifactItem
                {
                    Category = ArtifactCategory.LogFiles,
                    Type = ArtifactType.File,
                    Location = @"C:\Windows\inf\setupapi.dev.log"
                }],
                "тестовый лог",
                null,
                null);

            PdfReportGenerator.Generate(request, path);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void GetAppVersion_MatchesAssembly()
    {
        Assert.Equal(AppInfo.Version, ReportExportService.GetAppVersion());
    }

    [Fact]
    public void BuildUsbReport_GhostCleanOperation()
    {
        var request = ReportExportService.BuildUsbReport(
            ReportOperationType.GhostClean, [], "log", null, null);
        Assert.Equal(ReportOperationType.GhostClean, request.Operation);
    }

    [Fact]
    public void PdfReportGenerator_WritesNetworkReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"utc_net_{Guid.NewGuid():N}.pdf");
        try
        {
            var request = ReportExportService.BuildNetworkReport(
                ReportOperationType.Clean,
                [new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.WiFiProfile,
                    FilterGroup = NetworkAuditFilterGroup.WiFi,
                    Source = "netsh",
                    Title = "Wi‑Fi test",
                    Location = "home",
                    CanClean = true
                }],
                "лог",
                new NetworkAuditOptions(),
                null);

            PdfReportGenerator.Generate(request, path);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
