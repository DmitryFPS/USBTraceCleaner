using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task Cleaner_Simulation_DoesNotChangeUsbStor()
    {
        if (!TestPrerequisites.IsAdmin || !TestPrerequisites.HasUsbStor) return;

        var scanner = new ArtifactScanner();
        var cleaner = new ArtifactCleaner();
        var items = scanner.Scan(new CleanupOptions());

        var before = RegistryHelper.CountUsbStorDevices();
        var result = await cleaner.ExecuteAsync(items, new CleanupOptions
        {
            SimulationMode = true,
            SaveBackup = false,
            CreateRestorePoint = false,
            CloseExplorer = false,
            RebootAfterClean = false
        });
        var after = RegistryHelper.CountUsbStorDevices();

        Assert.True(result.Success);
        Assert.Equal(before, after);
        Assert.Contains("[SIM]", result.Log);
    }

    [Fact]
    public async Task Cleaner_RealRun_ClearsUsbStor()
    {
        if (!TestPrerequisites.CanRunDestructiveUsbStorTest) return;

        var scanner = new ArtifactScanner();
        var cleaner = new ArtifactCleaner();
        var items = scanner.Scan(new CleanupOptions { CleanAllUsbDevices = false });

        Assert.NotEmpty(items);

        var beforeAll = RegistryHelper.CountUsbStorDevicesAllControlSets();
        Assert.True(beforeAll > 0);

        var result = await cleaner.ExecuteAsync(items, new CleanupOptions
        {
            SimulationMode = false,
            SaveBackup = false,
            CreateRestorePoint = false,
            CloseExplorer = false,
            RebootAfterClean = false,
            CleanAllUsbDevices = false,
            ExportFullUsbEnum = false,
            CleanEventLogs = false
        });

        var after = RegistryHelper.CountUsbStorDevices();

        Assert.True(result.Success);
        Assert.Equal(0, after);
        Assert.Contains("USBSTOR", result.Log);
    }
}
