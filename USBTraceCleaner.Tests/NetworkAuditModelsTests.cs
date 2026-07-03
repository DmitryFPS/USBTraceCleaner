using USBTraceCleaner.Models;

namespace USBTraceCleaner.Tests;

public class NetworkAuditModelsTests
{
    [Fact]
    public void Whitelist_ParseSplitsByCommaAndSemicolon()
    {
        var wl = NetworkAuditWhitelist.Parse("20.0.0.1, 20.0.0.2;20.0.0.3", "net1;net2", "vpn1");
        Assert.Equal(3, wl.AllowedIps.Count);
        Assert.Equal(2, wl.AllowedWiFi.Count);
        Assert.Equal(1, wl.AllowedVpn.Count);
    }

    [Fact]
    public void Whitelist_DefaultExample_HasExpectedEntries()
    {
        var wl = NetworkAuditWhitelist.DefaultExample();
        Assert.Contains("doZOR", wl.AllowedWiFi);
        Assert.Contains("HAPP", wl.AllowedVpn);
    }

    [Fact]
    public void NetworkAuditItem_DisplayProperties()
    {
        var item = new NetworkAuditItem
        {
            Kind = NetworkAuditKind.WiFiProfile,
            FilterGroup = NetworkAuditFilterGroup.WiFi,
            Source = "netsh",
            Title = "Профиль",
            Detail = "Auth: WPA2",
            Secret = "secret123",
            Location = "home",
            CanClean = true,
            EventTime = new DateTime(2026, 7, 1, 12, 0, 0),
            AuthorizationStatus = NetworkAuthorizationStatus.Allowed,
            MaskSecrets = true
        };

        Assert.Equal("Wi‑Fi", item.DisplayGroup);
        Assert.Equal("Разрешено", item.DisplayAuthorization);
        Assert.Equal("01.07.2026 12:00:00", item.DisplayTime);
        Assert.Contains("••••••••", item.DisplayDetail);
        Assert.Equal("Удалить", item.ActionLabel);
        Assert.Contains("Wi‑Fi", item.CleanEffect);
    }

    [Fact]
    public void NetworkAuditItem_InfoOnlyCleanEffect()
    {
        var item = new NetworkAuditItem
        {
            Kind = NetworkAuditKind.RouterGateway,
            FilterGroup = NetworkAuditFilterGroup.Router,
            Source = "router",
            Title = "Шлюз",
            Location = "192.168.1.1",
            CanClean = false
        };

        Assert.Equal("Просмотр", item.ActionLabel);
        Assert.Contains("Только информация", item.CleanEffect);
    }
}
