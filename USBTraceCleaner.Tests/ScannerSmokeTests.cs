using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class ScannerSmokeTests
{
    [Fact]
    public void OtherUsbTraceScanner_Scan_Completes()
    {
        var items = new OtherUsbTraceScanner().Scan(log: _ => { });
        Assert.NotNull(items);
    }

    [Fact]
    public void OtherUsbPathCollector_CollectsForVidPid()
    {
        var paths = OtherUsbPathCollector.CollectRegistryPaths("0E0F", "0003");
        Assert.NotNull(paths);
    }

    [Fact]
    public void PnPGhostScanner_Scan_Completes()
    {
        var items = PnPGhostScanner.Scan();
        Assert.NotNull(items);
    }

    [Fact]
    public void AppPaths_ReturnsDirectory()
    {
        var dir = AppPaths.GetExeDirectory();
        Assert.False(string.IsNullOrWhiteSpace(dir));
        Assert.True(Directory.Exists(dir));
    }
}
