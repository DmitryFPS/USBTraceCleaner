using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class CleanerSimulationTests
{
    [Fact]
    public async Task ArtifactCleaner_Simulation_WithEmptyList()
    {
        var cleaner = new ArtifactCleaner();
        var result = await cleaner.ExecuteAsync([], new CleanupOptions
        {
            SimulationMode = true,
            SaveBackup = false,
            CreateRestorePoint = false,
            CloseExplorer = false,
            RebootAfterClean = false
        });

        Assert.True(result.Success);
        Assert.Contains("СИМУЛЯЦИЯ", result.Log);
    }

    [Fact]
    public async Task ArtifactCleaner_Simulation_WithScannedItems()
    {
        if (!TestPrerequisites.IsAdmin) return;

        var items = new ArtifactScanner().Scan(new CleanupOptions { SimulationMode = true });
        var result = await new ArtifactCleaner().ExecuteAsync(
            items.Take(10),
            new CleanupOptions
            {
                SimulationMode = true,
                SaveBackup = false,
                CreateRestorePoint = false,
                CloseExplorer = false,
                RebootAfterClean = false,
                CleanEventLogs = false
            });

        Assert.True(result.Success);
        Assert.Contains("[SIM]", result.Log);
    }

    [Fact]
    public void NetworkAuditCleaner_Simulation_SkipsDeletion()
    {
        var cleaner = new NetworkAuditCleaner();
        var items = new[]
        {
            new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh",
                Title = "test",
                Location = "test",
                CanClean = true,
                Selected = true
            }
        };

        var result = cleaner.Execute(items, new NetworkAuditOptions { SimulationMode = true });
        Assert.Contains("симуляции", result.Log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtherUsbTraceCleaner_WithoutAdmin_ReturnsError()
    {
        if (TestPrerequisites.IsAdmin) return;

        var result = OtherUsbTraceCleaner.Execute(
            [new OtherUsbTraceItem { Vid = "FFFF", Pid = "FFFF" }],
            simulation: true);

        Assert.False(result.Success);
        Assert.Contains("администратора", result.ErrorMessage);
    }
}
