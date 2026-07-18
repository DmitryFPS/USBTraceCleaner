using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class ForensicTracePatternsTests
{
    [Theory]
    [InlineData("USBSTOR#Disk&Ven_...", true)]
    [InlineData("SWD\\WPDBUSENUM\\__USBSTOR#DISK", true)]
    [InlineData("C:\\Windows\\notepad.exe", false)]
    public void ContainsUsbStorageToken(string text, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.ContainsUsbStorageToken(text));

    [Theory]
    [InlineData("USBTRACECLEANER.EXE-ABC.pf", true)]
    [InlineData("USBOBLIVION64.EXE-1.pf", true)]
    [InlineData("NOTEPAD.EXE-1.pf", false)]
    public void IsPrefetchOfInterest(string name, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.IsPrefetchOfInterest(name));

    [Theory]
    [InlineData("AutomaticDestinations", true)]
    [InlineData("CustomDestinations", true)]
    [InlineData("AutomaticDestinations-ms", false)]
    public void JumpListFolderNameIsValid(string name, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.JumpListFolderNameIsValid(name));

    [Fact]
    public void Rot13_RoundTrip()
    {
        var original = "C:\\Users\\adm\\USBTraceCleaner.exe";
        var encoded = ForensicTracePatterns.Rot13(original);
        Assert.NotEqual(original, encoded);
        Assert.Equal(original, ForensicTracePatterns.Rot13(encoded));
    }

    [Theory]
    [InlineData("048DC1930008", "048D", "C193")]
    [InlineData("IgnoreHWSerNum17EFF006", "17EF", "F006")]
    [InlineData("30C900F80016", "30C9", "00F8")]
    [InlineData("bad", "", "")]
    public void TryParseUsbFlagsName(string name, string vid, string pid)
    {
        var ok = ForensicTracePatterns.TryParseUsbFlagsName(name, out var v, out var p);
        if (string.IsNullOrEmpty(vid))
        {
            Assert.False(ok);
            return;
        }

        Assert.True(ok);
        Assert.Equal(vid, v);
        Assert.Equal(pid, p);
    }

    [Fact]
    public void ContainsSelfTraceToken_DetectsCleaners()
    {
        Assert.True(ForensicTracePatterns.ContainsSelfTraceToken(
            @"\Device\HarddiskVolume3\Users\adm\Desktop\regedit\USBOblivion64.exe"));
        Assert.True(ForensicTracePatterns.IsUsbOrSelfTrace("USBTraceCleaner.exe"));
        Assert.False(ForensicTracePatterns.ContainsSelfTraceToken("chrome.exe"));
    }
}
