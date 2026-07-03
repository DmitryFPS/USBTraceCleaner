using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkScannerSmokeTests
{
    [Fact]
    public void NetworkAuditScanner_FinalizeScan_ReturnsItems()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "a",
                Title = "t1",
                Location = "loc",
                CanClean = true
            }
        };

        var result = NetworkAuditScanner.FinalizeScan(items, new NetworkAuditOptions());
        Assert.NotEmpty(result);
    }
}
