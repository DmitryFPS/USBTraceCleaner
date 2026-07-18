using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditHintsTests
{
    [Fact]
    public void BuildCleanupWarning_SelectiveMode()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "Wi‑Fi",
                Location = "x",
                CanClean = true,
                Selected = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Unknown
            }
        };

        var text = NetworkAuditHints.BuildCleanupWarning(items, fullClean: false);
        Assert.Contains("Выборочная очистка", text);
        Assert.Contains("К очистке (вне белого списка): 1", text);
        Assert.Contains("Элементы белого списка не удаляются", text);
    }

    [Fact]
    public void BuildCleanupWarning_FullCleanMode()
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
                CanClean = true,
                Selected = true
            }
        };

        var text = NetworkAuditHints.BuildCleanupWarning(items, fullClean: true);
        Assert.Contains("МАКСИМАЛЬНАЯ ОЧИСТКА", text);
        Assert.Contains("DNS-кэш: да", text);
        Assert.Contains("перезагрузится", text);
    }

    [Fact]
    public void HelpText_ContainsSections()
    {
        Assert.Contains("Wi‑Fi", NetworkAuditHints.HelpText);
        Assert.Contains("hosts", NetworkAuditHints.HostsWarning);
        Assert.Contains("не удаляются", NetworkAuditHints.HelpText);
        Assert.DoesNotContain("всё равно удаляется", NetworkAuditHints.HelpText);
    }

    [Fact]
    public void AppInfo_VersionMatchesProject()
    {
        Assert.Equal("1.7.1", AppInfo.Version);
        Assert.Equal("v1.7.1", AppInfo.VersionLabel);
    }
}
