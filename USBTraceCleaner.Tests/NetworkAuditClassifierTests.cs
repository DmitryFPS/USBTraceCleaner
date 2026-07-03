using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditClassifierTests
{
    private static NetworkAuditItem Item(string title, string detail = "", string location = "", string source = "") =>
        new()
        {
            Kind = NetworkAuditKind.WiFiProfile,
            FilterGroup = NetworkAuditFilterGroup.WiFi,
            Source = source,
            Title = title,
            Detail = detail,
            Location = location,
            CanClean = true
        };

    [Fact]
    public void Classify_AllowedByWiFiName()
    {
        var wl = new NetworkAuditWhitelist { AllowedWiFi = ["doZOR"] };
        var status = NetworkAuditClassifier.Classify(Item("Профиль Wi‑Fi: doZOR", location: "doZOR"), wl);
        Assert.Equal(NetworkAuthorizationStatus.Allowed, status);
    }

    [Fact]
    public void Classify_AllowedByIpInDetail()
    {
        var wl = new NetworkAuditWhitelist { AllowedIps = ["20.20.20.76"] };
        var status = NetworkAuditClassifier.Classify(Item("Шлюз", detail: "IP: 20.20.20.76"), wl);
        Assert.Equal(NetworkAuthorizationStatus.Allowed, status);
    }

    [Fact]
    public void Classify_AllowedByVpn()
    {
        var wl = new NetworkAuditWhitelist { AllowedVpn = ["HAPP"] };
        var status = NetworkAuditClassifier.Classify(Item("VPN HAPP tunnel"), wl);
        Assert.Equal(NetworkAuthorizationStatus.Allowed, status);
    }

    [Fact]
    public void Classify_UnknownWhenNotInWhitelist()
    {
        var wl = new NetworkAuditWhitelist { AllowedWiFi = ["home"] };
        var status = NetworkAuditClassifier.Classify(Item("Cafe_Free"), wl);
        Assert.Equal(NetworkAuthorizationStatus.Unknown, status);
    }

    [Fact]
    public void ApplyWhitelist_SetsStatusOnAllItems()
    {
        var items = new[]
        {
            Item("doZOR", location: "doZOR"),
            Item("Cafe_Free", location: "Cafe_Free")
        };
        var wl = new NetworkAuditWhitelist { AllowedWiFi = ["doZOR"] };

        NetworkAuditClassifier.ApplyWhitelist(items, wl);

        Assert.Equal(NetworkAuthorizationStatus.Allowed, items[0].AuthorizationStatus);
        Assert.Equal(NetworkAuthorizationStatus.Unknown, items[1].AuthorizationStatus);
    }
}
