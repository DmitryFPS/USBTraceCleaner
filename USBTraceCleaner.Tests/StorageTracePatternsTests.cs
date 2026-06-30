using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class StorageTracePatternsTests
{
    [Theory]
    [InlineData(@"USBSTOR\Disk&Ven_Kingston&Prod...", true)]
    [InlineData(@"STORAGE#RemovableMedia", true)]
    [InlineData(@"USB\VID_046D&PID_C077", false)]
    [InlineData(@"Integrated Camera", false)]
    [InlineData(null, false)]
    public void MatchesStorage_DetectsStorageMarkers(string? text, bool expected)
    {
        Assert.Equal(expected, StorageTracePatterns.MatchesStorage(text));
    }

    [Theory]
    [InlineData("WUDFWpdMtp", true)]
    [InlineData("USBSTOR", false)]
    public void MatchesMtp_DetectsMtpMarkers(string text, bool expected)
    {
        Assert.Equal(expected, StorageTracePatterns.MatchesMtp(text));
    }

    [Theory]
    [InlineData("USBSTOR#Disk", false, true)]
    [InlineData("USBSTOR#Disk", true, true)]
    [InlineData("WUDFWpdMtp", false, false)]
    [InlineData("WUDFWpdMtp", true, true)]
    [InlineData("HidUsb", false, false)]
    public void MatchesDeviceMigrationEntry_RespectsMtpFlag(string text, bool includeMtp, bool expected)
    {
        Assert.Equal(expected, StorageTracePatterns.MatchesDeviceMigrationEntry(text, includeMtp));
    }
}
