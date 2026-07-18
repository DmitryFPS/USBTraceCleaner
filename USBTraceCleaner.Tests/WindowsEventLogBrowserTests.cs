using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class WindowsEventLogBrowserTests
{
    [Theory]
    [InlineData("Device USBSTOR\\DiskVen_General installed", true)]
    [InlineData("WPDBUSENUM volume mounted", true)]
    [InlineData("Chrome updated successfully", false)]
    public void LooksUsbRelated_MessageTokens(string message, bool expected) =>
        Assert.Equal(expected, WindowsEventLogBrowser.LooksUsbRelated(message));

    [Theory]
    [InlineData("System", "Журналы Windows")]
    [InlineData("Application", "Журналы Windows")]
    [InlineData("Security", "Журналы Windows")]
    [InlineData("Microsoft-Windows-Kernel-PnP/Configuration", "Microsoft")]
    [InlineData("Microsoft-Windows-UserPnp/DeviceInstall", "Microsoft")]
    [InlineData("Intel-GFX-Driver/Operational", "Intel")]
    [InlineData("OpenSSH/Operational", "OpenSSH")]
    [InlineData("Windows PowerShell", "Windows PowerShell")]
    [InlineData("HardwareEvents", "HardwareEvents")]
    public void ClassifyGroup_MatchesEventViewerStyle(string channel, string expectedGroup) =>
        Assert.Equal(expectedGroup, WindowsEventLogBrowser.ClassifyGroup(channel));

    [Fact]
    public void FriendlyLabel_UsesRussianForClassicWindowsLogs()
    {
        Assert.Equal("Система", WindowsEventLogBrowser.FriendlyLabel("System"));
        Assert.Equal("Безопасность", WindowsEventLogBrowser.FriendlyLabel("Security"));
        Assert.Equal("Configuration", WindowsEventLogBrowser.FriendlyLabel("Microsoft-Windows-Kernel-PnP/Configuration"));
    }

    [Fact]
    public void ChannelFamily_UsesPrefixBeforeSlash()
    {
        var row = new EventLogChannelRow
        {
            Channel = "Microsoft-Windows-Kernel-PnP/Configuration",
            Label = "Configuration",
            Group = "Microsoft",
        };
        Assert.Equal("Microsoft-Windows-Kernel-PnP", row.ChannelFamily);
        Assert.Equal("Configuration", row.DisplayName);
    }

    [Fact]
    public void ListChannels_ReturnsDynamicLogsFromThisMachine()
    {
        var list = WindowsEventLogBrowser.ListChannels();
        Assert.NotEmpty(list);
        Assert.Contains(list, c => c.Channel.Equals("System", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(list, c => c.Group == WindowsEventLogBrowser.GroupWindowsLogs);
        // На разных ПК набор разный — главное, что не захардкоженный короткий каталог
        Assert.True(list.Count >= 10, $"Expected many channels, got {list.Count}");
    }
}
