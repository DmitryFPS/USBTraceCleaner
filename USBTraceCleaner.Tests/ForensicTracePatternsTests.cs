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

    [Theory]
    [InlineData("SWD#WPDBUSENUM#_??_USBSTOR#Disk&Ven_General&Prod_UDisk", true)]
    [InlineData("SWD#WPDBUSENUM#something", true)]
    [InlineData("SomeBluetoothDevice", false)]
    [InlineData("", false)]
    public void IsWindowsPortableDeviceUsbChild(string name, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.IsWindowsPortableDeviceUsbChild(name));

    [Theory]
    [InlineData("USBSTOR", true)]
    [InlineData("UASPStor", true)]
    [InlineData("WUDFWpdMtp", true)]
    [InlineData("HidUsb", false)]
    [InlineData("usbvideo", false)]
    [InlineData(null, false)]
    public void IsRemovableUsbService(string? service, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.IsRemovableUsbService(service));

    [Theory]
    [InlineData(@"\??\USBSTOR#Disk&Ven_General&Prod_UDisk#123", true)]
    [InlineData(@"\??\SWD#WPDBUSENUM#_??_USBSTOR#Disk&Ven_X", true)]
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData("", false)]
    public void IsMountedDevicesUsbPath(string path, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.IsMountedDevicesUsbPath(path));

    [Fact]
    public void IsMountedDevicesUsbData_Utf16UsbStor()
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(
            @"\??\USBSTOR#Disk&Ven_General&Prod_UDiskRev_5.00#6cadd1461_0");
        Assert.True(ForensicTracePatterns.IsMountedDevicesUsbData(bytes));
        Assert.False(ForensicTracePatterns.IsMountedDevicesUsbData(
            System.Text.Encoding.Unicode.GetBytes(@"\??\SCSI#Disk&Ven_NVMe")));
    }

    [Theory]
    [InlineData(@"\DosDevices\H:", 0, true)]
    [InlineData(@"\DosDevices\C:", 0, false)]
    [InlineData(@"\??\Volume{abc}", 0, false)]
    public void IsOrphanDosDeviceLetterName(string name, int mask, bool expected) =>
        Assert.Equal(expected, ForensicTracePatterns.IsOrphanDosDeviceLetterName(name, mask));

    [Fact]
    public void IsJumpListContentOfInterest_UsbOnly_NotEverything()
    {
        // driveMask=0 → все буквы D–Z считаются «отключёнными» removable
        Assert.True(ForensicTracePatterns.IsJumpListContentOfInterest(
            @"E:\photos\IMG_001.jpg", connectedDriveMask: 0, includeSelfTraces: false));
        Assert.True(ForensicTracePatterns.IsJumpListContentOfInterest(
            "USBSTOR#Disk&Ven_General", connectedDriveMask: 0, includeSelfTraces: false));
        Assert.True(ForensicTracePatterns.IsJumpListContentOfInterest(
            "General UDisk volume", connectedDriveMask: 0, includeSelfTraces: false));

        Assert.False(ForensicTracePatterns.IsJumpListContentOfInterest(
            @"C:\Users\adm\Documents\report.docx", connectedDriveMask: 0, includeSelfTraces: false));
        Assert.False(ForensicTracePatterns.IsJumpListContentOfInterest(
            "chrome.exe recent file", connectedDriveMask: 0, includeSelfTraces: false));

        Assert.True(ForensicTracePatterns.IsJumpListContentOfInterest(
            "USBTraceCleaner.exe", connectedDriveMask: 0, includeSelfTraces: true));
        Assert.False(ForensicTracePatterns.IsJumpListContentOfInterest(
            "USBTraceCleaner.exe", connectedDriveMask: 0, includeSelfTraces: false));
    }
}
