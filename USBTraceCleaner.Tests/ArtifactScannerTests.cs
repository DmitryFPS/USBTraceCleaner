using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class ArtifactScannerTests
{
    [Fact]
    public void Scan_FindsUsbStorWhenDevicesPresent()
    {
        if (!TestPrerequisites.IsAdmin || !TestPrerequisites.HasUsbStor) return;

        var scanner = new ArtifactScanner();
        var items = scanner.Scan(new CleanupOptions { SimulationMode = true });

        Assert.Contains(items, i =>
            i.Location.Contains(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_SimulationMode_ReturnsItemsWithoutSideEffects()
    {
        if (!TestPrerequisites.IsAdmin) return;

        var before = RegistryHelper.CountUsbStorDevices();
        var scanner = new ArtifactScanner();
        var items = scanner.Scan(new CleanupOptions { SimulationMode = true });
        var after = RegistryHelper.CountUsbStorDevices();

        Assert.Equal(before, after);
        Assert.NotEmpty(items);
    }
}
