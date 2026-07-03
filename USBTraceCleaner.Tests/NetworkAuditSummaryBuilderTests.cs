using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;
using Xunit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditSummaryBuilderTests
{
    [Fact]
    public void Build_GroupsWhitelistAndUnknownWiFi()
    {
        var whitelist = new NetworkAuditWhitelist
        {
            AllowedWiFi = ["doZOR"],
            AllowedVpn = ["HAPP"]
        };

        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "Профиль Wi‑Fi: doZOR",
                Detail = "Auth: WPA2 | Последнее: 01.07.2026",
                Location = "doZOR",
                CanClean = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Allowed
            },
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "Профиль Wi‑Fi: Cafe_Free",
                Detail = "Auth: Open",
                Location = "Cafe_Free",
                CanClean = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Unknown
            }
        };

        var summary = NetworkAuditSummaryBuilder.Build(items, whitelist);
        var text = summary.ToPlainText();

        Assert.Contains("doZOR", text);
        Assert.Contains("Cafe_Free", text);
        Assert.Contains("НЕ в белом списке", text);
        Assert.Contains("ваша сеть", text);
    }

    [Fact]
    public void Build_WithCleanableItems_HasCleanupSection()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.DnsCache,
                FilterGroup = NetworkAuditFilterGroup.Dns,
                Source = "dns",
                Title = "DNS",
                Location = "__flushdns__",
                CanClean = true
            }
        };

        var text = NetworkAuditSummaryBuilder.Build(items).ToPlainText();
        Assert.Contains("Всего записей", text);
    }
}
