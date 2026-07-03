using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditSummaryBuilderExtendedTests
{
    [Fact]
    public void Build_EmptyList_StillHasOverview()
    {
        var summary = NetworkAuditSummaryBuilder.Build([]);
        Assert.NotEmpty(summary.Sections);
        Assert.Contains("Всего записей", summary.ToPlainText());
    }

    [Fact]
    public void Build_IncludesVpnAndEthernetEvents()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.VpnEvent,
                FilterGroup = NetworkAuditFilterGroup.Vpn,
                Source = "journal",
                Title = "VPN подключение",
                Location = "x",
                EventTime = DateTime.Today,
                CanClean = false,
                AuthorizationStatus = NetworkAuthorizationStatus.Unknown
            },
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.EthernetAdapter,
                FilterGroup = NetworkAuditFilterGroup.Ethernet,
                Source = "netsh",
                Title = "Ethernet",
                Location = "eth0",
                CanClean = false
            }
        };

        var text = NetworkAuditSummaryBuilder.Build(items).ToPlainText();
        Assert.Contains("VPN", text);
        Assert.Contains("Ethernet", text);
    }
}
