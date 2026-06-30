using System.Text;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class LogFileScrubberTests
{
    [Fact]
    public void ScrubSetupApiContent_RemovesUsbStorLines()
    {
        var input = """
            [Device Install Log]
            >>>  [Device Install (USBSTOR) - USB\VID_ABCD&PID_1234\serial]
            some detail
            <<<  Section end
            >>>  [Device Install (Net) - PCI\VEN_8086]
            keep this
            """;

        var output = new StringBuilder();
        var removed = LogFileScrubber.ScrubSetupApiContent(input, output);
        var text = output.ToString();

        Assert.True(removed >= 2);
        Assert.DoesNotContain("USBSTOR", text);
        Assert.Contains("PCI\\VEN_8086", text);
    }

    [Fact]
    public void IsManagedLogFile_SetupApiDevLog_True()
    {
        Assert.True(LogFileScrubber.IsManagedLogFile(@"C:\Windows\inf\setupapi.dev.log"));
        Assert.False(LogFileScrubber.IsManagedLogFile(@"C:\Windows\System32\kernel32.dll"));
    }
}
