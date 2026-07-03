using USBTraceCleaner.Models;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class NetworkAuditDisplayTests
{
    [Fact]
    public void CollapseDetail_JoinsLines()
    {
        var text = "строка1\nстрока2\tстрока3";
        Assert.Equal("строка1 | строка2 | строка3", NetworkAuditDisplay.CollapseDetail(text));
    }

    [Fact]
    public void SanitizeForDisplay_RemovesReplacementChar()
    {
        Assert.Equal("abc", NetworkAuditDisplay.SanitizeForDisplay("a\uFFFDbc"));
    }

    [Theory]
    [InlineData(true, "Удалить")]
    [InlineData(false, "Просмотр")]
    public void GetActionLabel(bool canClean, string expected)
    {
        Assert.Equal(expected, NetworkAuditDisplay.GetActionLabel(canClean));
    }

    [Fact]
    public void GetCleanEffect_WiFiProfile()
    {
        var effect = NetworkAuditDisplay.GetCleanEffect(NetworkAuditKind.WiFiProfile, "Профиль");
        Assert.Contains("Wi‑Fi", effect);
    }

    [Theory]
    [InlineData("Очистить DNS-кэш", "DNS")]
    [InlineData("Очистить журнал событий", "журнал")]
    [InlineData("Очистить NetBIOS", "NetBIOS")]
    public void GetCleanEffect_ClearTitleKeywords(string title, string fragment)
    {
        var effect = NetworkAuditDisplay.GetCleanEffect(NetworkAuditKind.DnsCache, title);
        Assert.Contains(fragment, effect, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCleanEffect_ClearTitle_Generic()
    {
        var effect = NetworkAuditDisplay.GetCleanEffect(NetworkAuditKind.RegistryTrace, "Очистить след");
        Assert.Equal("Удаление выбранного следа", effect);
    }

    [Fact]
    public void SanitizeForDisplay_StripsControlChars()
    {
        Assert.Equal("ab", NetworkAuditDisplay.SanitizeForDisplay("a\u0001b"));
    }

    [Fact]
    public void CollapseDetail_EmptyInput()
    {
        Assert.Equal(string.Empty, NetworkAuditDisplay.CollapseDetail(null));
        Assert.Equal(string.Empty, NetworkAuditDisplay.CollapseDetail("   "));
    }
}
