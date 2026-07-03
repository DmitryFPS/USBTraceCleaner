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
        Assert.Contains("Будет очищено элементов: 1", text);
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
    }

    [Fact]
    public void AppInfo_VersionMatchesProject()
    {
        Assert.StartsWith("1.", AppInfo.Version);
        Assert.StartsWith("v1.", AppInfo.VersionLabel);
    }
}
