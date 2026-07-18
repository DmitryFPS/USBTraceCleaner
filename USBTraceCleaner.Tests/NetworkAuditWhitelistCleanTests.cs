using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditWhitelistCleanTests
{
    [Fact]
    public void NetworkAuditCleaner_SkipsWhitelistedItems()
    {
        var cleaner = new NetworkAuditCleaner();
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
                Selected = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Allowed
            }
        };

        var result = cleaner.Execute(items, new NetworkAuditOptions
        {
            SimulationMode = false,
            FullCleanMode = false,
            DisconnectNetwork = false,
            RebootAfterClean = false
        });

        Assert.Contains("белый список", result.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Processed);
    }

    [Theory]
    [InlineData("doZOR", new[] { "doZOR" }, true)]
    [InlineData("Guest-WiFi", new[] { "doZOR" }, false)]
    [InlineData("HAPP-tun", new[] { "happ" }, true)]
    [InlineData("", new[] { "doZOR" }, false)]
    public void IsWhitelistedName_MatchesExpected(string name, string[] allowed, bool expected)
    {
        Assert.Equal(expected, NetworkPostCleanActions.IsWhitelistedName(name, allowed));
    }

    [Fact]
    public void BuildCleanupWarning_MentionsWhitelistPreservation()
    {
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "home",
                Location = "home",
                CanClean = true,
                Selected = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Allowed
            }
        };

        var text = NetworkAuditHints.BuildCleanupWarning(items, fullClean: false);
        Assert.Contains("белый список", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("К очистке", text);
        Assert.DoesNotContain("всё равно удаляется", text);
    }
}
