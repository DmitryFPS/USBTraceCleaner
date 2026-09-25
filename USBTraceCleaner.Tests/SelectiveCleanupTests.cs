using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class SelectiveCleanupTests
{
    private static ArtifactItem Item(string path, ArtifactType type = ArtifactType.File, bool selected = true) => new()
    {
        Category = ArtifactCategory.FileSystem, Type = type, Location = path, Selected = selected
    };

    [Fact]
    public void Defaults_DoNotDeleteOrReboot()
    {
        var options = new CleanupOptions();
        Assert.True(options.SimulationMode);
        Assert.False(options.RebootAfterClean);
        Assert.False(options.CleanVolumeShadowCopies);
    }

    [Fact]
    public async Task Simulation_ProcessesOnlySelected_AndIncludesEventLogs()
    {
        var result = await new ArtifactCleaner().ExecuteAsync(
            [Item("chosen"), Item("unselected", selected: false), Item("TestChannel", ArtifactType.EventLog)],
            new CleanupOptions { SimulationMode = true });
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalFound);
        Assert.Equal(2, result.ItemsProcessed);
        Assert.Contains("[SIM] LOG  TestChannel", result.Log);
        Assert.DoesNotContain("unselected", result.Log);
    }

    [Fact]
    public async Task DisabledEventLogs_AreNotProcessed()
    {
        var result = await new ArtifactCleaner().ExecuteAsync([Item("TestChannel", ArtifactType.EventLog)],
            new CleanupOptions { SimulationMode = true, CleanEventLogs = false });
        Assert.True(result.Success);
        Assert.Equal(0, result.ItemsProcessed);
        Assert.DoesNotContain("TestChannel", result.Log);
    }

    [Fact]
    public async Task DisabledSystemLog_IsNotProcessed()
    {
        var result = await new ArtifactCleaner().ExecuteAsync([Item("System", ArtifactType.EventLog)],
            new CleanupOptions { SimulationMode = true, CleanSystemEventLog = false });
        Assert.Equal(0, result.ItemsProcessed);
    }

    [Fact]
    public async Task ReusedCleaner_DoesNotRetainPreviousLogOrFailures()
    {
        var cleaner = new ArtifactCleaner();
        var failed = await cleaner.ExecuteAsync([Item("unsupported", ArtifactType.Directory)],
            new CleanupOptions { SimulationMode = true });
        Assert.False(failed.Success);
        Assert.Equal(1, failed.FailedCount);
        Assert.NotNull(failed.ErrorMessage);
        var next = await cleaner.ExecuteAsync([], new CleanupOptions { SimulationMode = true });
        Assert.True(next.Success);
        Assert.Equal(0, next.FailedCount);
        Assert.DoesNotContain("unsupported", next.Log);
    }

    [Fact]
    public async Task CancellationBeforeStart_DoesNotProcessAnything()
    {
        var cleaner = new ArtifactCleaner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleaner.ExecuteAsync(
            [Item("not-processed")], new CleanupOptions { SimulationMode = true },
            cancellationToken: new CancellationToken(true)));
        Assert.Empty(cleaner.LogOutput);
    }

    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\live")]
    [InlineData(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1234&PID_5678\live\Properties")]
    public void PresentDevice_ProtectsInstanceAndItsProperties(string path)
    {
        Assert.True(DeviceRegistryGuard.ContainsPresentDevice(path,
            id => id == @"USB\VID_1234&PID_5678\live", _ => []));
    }

    [Fact]
    public void PresentDevice_ProtectsParentTree()
    {
        Assert.True(DeviceRegistryGuard.ContainsPresentDevice(@"SYSTEM\ControlSet001\Enum\USBSTOR",
            id => id == @"USBSTOR\Disk\live",
            path => path.EndsWith("USBSTOR") ? ["Disk"] : ["offline", "live"]));
    }

    [Fact]
    public void OfflineDevice_CanBeProcessed()
    {
        Assert.False(DeviceRegistryGuard.ContainsPresentDevice(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk\offline",
            _ => false, _ => throw new Exception("Must not enumerate instance properties")));
    }

    [Fact]
    public void RegistryReadFailure_IsNotTreatedAsDisconnected()
    {
        Assert.Throws<UnauthorizedAccessException>(() => DeviceRegistryGuard.ContainsPresentDevice(
            @"SYSTEM\ControlSet001\Enum\USBSTOR", _ => false,
            _ => throw new UnauthorizedAccessException()));
    }
}
