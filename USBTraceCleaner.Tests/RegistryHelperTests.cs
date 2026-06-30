using Microsoft.Win32;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class RegistryHelperTests
{
    private const string TestRoot = @"SOFTWARE\USBTraceCleanerTest";

    [Fact]
    public void EnumerateControlSets_FindsAtLeastOne()
    {
        var sets = RegistryHelper.EnumerateControlSets().ToList();
        Assert.NotEmpty(sets);
        Assert.Contains(sets, s => s.Equals("ControlSet001", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeleteKey_RemovesTestTree()
    {
        if (!TestPrerequisites.IsAdmin) return;

        CreateTestTree();

        Assert.True(RegistryHelper.KeyExists(RegistryHive.LocalMachine, $@"{TestRoot}\Child\Leaf"));

        var ok = RegistryHelper.DeleteKey(RegistryHive.LocalMachine, TestRoot, simulation: false);
        Assert.True(ok);
        Assert.False(RegistryHelper.KeyExists(RegistryHive.LocalMachine, TestRoot));
    }

    [Fact]
    public void DeleteKey_SimulationDoesNotRemove()
    {
        if (!TestPrerequisites.IsAdmin) return;

        CreateTestTree();
        RegistryHelper.DeleteKey(RegistryHive.LocalMachine, TestRoot, simulation: true);
        Assert.True(RegistryHelper.KeyExists(RegistryHive.LocalMachine, TestRoot));

        RegistryHelper.DeleteKey(RegistryHive.LocalMachine, TestRoot, simulation: false);
    }

    private static void CreateTestTree()
    {
        RegistryHelper.DeleteKey(RegistryHive.LocalMachine, TestRoot, simulation: false);

        using var root = Registry.LocalMachine.CreateSubKey(TestRoot);
        using var child = root!.CreateSubKey("Child");
        child!.CreateSubKey("Leaf");
    }
}
