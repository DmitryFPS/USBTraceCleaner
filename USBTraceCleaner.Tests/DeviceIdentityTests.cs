using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Tests;

public class DeviceIdentityTests
{
    private static ArtifactItem Key(string path) => new()
    { Category = ArtifactCategory.RegistrySystem, Type = ArtifactType.RegistryKey, Location = path };

    [Fact]
    public void BasicScan_ProducesNamedUnselectedRecordsWithoutElevation()
    {
        var records = new ArtifactScanner().Scan(new CleanupOptions());
        Assert.All(records, item =>
        {
            Assert.False(item.Selected);
            Assert.False(string.IsNullOrWhiteSpace(item.DeviceName));
            Assert.Equal(ArtifactType.RegistryKey, item.Type);
            Assert.Contains(@"SYSTEM\CurrentControlSet\Enum\", item.Location);
        });
    }

    [Theory]
    [InlineData(@"SYSTEM\ControlSet001\Enum\USB\VID_1234&PID_5678\SERIAL1")]
    [InlineData(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1234&PID_5678\SERIAL1\Properties")]
    [InlineData(@"SYSTEM\ControlSet002\Control\DeviceClasses\{class}\##?#USB#VID_1234&PID_5678#SERIAL1#{interface}")]
    public void ExactInstance_ResolvesAcrossControlSetsAndInterfacePaths(string path)
    {
        var item = Key(path);
        new DeviceIdentityResolver([
            new(@"USB\VID_1234&PID_5678\SERIAL1", "My camera", Present: false),
            new(@"USB\VID_1234&PID_5678\SERIAL10", "Other camera", Present: true)
        ]).Enrich(item);
        Assert.Equal("My camera", item.DeviceName);
        Assert.False(item.DevicePresent);
        Assert.Single(item.RelatedDeviceIds);
    }

    [Fact]
    public void ShortSerial_DoesNotMatchDifferentPhysicalDevice()
    {
        var item = Key(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1234&PID_5678\SERIAL10");
        new DeviceIdentityResolver([new(@"USB\VID_1234&PID_5678\SERIAL1", "Wrong device")]).Enrich(item);
        Assert.Empty(item.RelatedDeviceIds);
        Assert.NotEqual("Wrong device", item.DeviceName);
    }

    [Fact]
    public void SharedContainer_ListsNamesAndProtectsAnyLiveMember()
    {
        var item = Key(@"SYSTEM\ControlSet001\Control\DeviceContainers\{1111}");
        new DeviceIdentityResolver([
            new(@"USB\VID_1234&PID_5678\A", "Phone", "{1111}", Present: false),
            new(@"SWD\WPDBUSENUM\B", "Phone storage", "{1111}", Present: true)
        ]).Enrich(item);
        Assert.Contains("Phone storage", item.DeviceName);
        Assert.True(item.DevicePresent);
        item.Selected = true;
        Assert.False(item.Selected);
        Assert.False(item.CanSelect);
    }

    [Fact]
    public void DriverAlias_ResolvesWithoutGuessingModel()
    {
        var item = Key(@"SYSTEM\ControlSet002\Control\Class\{driver}\0001");
        new DeviceIdentityResolver([new(@"USB\VID_1234&PID_5678\A", "Disk", Driver: @"{driver}\0001")]).Enrich(item);
        Assert.Equal("Disk", item.DeviceName);
    }

    [Fact]
    public void UsbFlags_AreLabeledAsModelMatch_NotAnExactInstance()
    {
        var item = Key(@"SYSTEM\ControlSet001\Control\usbflags\123456780100");
        new DeviceIdentityResolver([new(@"USB\VID_1234&PID_5678\A", "Disk")]).Enrich(item);
        Assert.Equal("Disk", item.DeviceName);
        Assert.Contains("конкретный экземпляр не определён", item.DeviceNameSource);
    }

    [Fact]
    public void MountedDeviceValue_CanResolveFromItsData()
    {
        var item = new ArtifactItem { Category = ArtifactCategory.RegistryMounted, Type = ArtifactType.RegistryValue,
            Location = @"SYSTEM\MountedDevices", ValueName = @"\DosDevices\E:" };
        new DeviceIdentityResolver([new(@"USBSTOR\Disk&Ven_SanDisk&Prod_Extreme&Rev_1\ABC&0", "SanDisk Extreme")])
            .Enrich(item, @"\??\USBSTOR#Disk&Ven_SanDisk&Prod_Extreme&Rev_1#ABC&0#{volume}");
        Assert.Equal("SanDisk Extreme", item.DeviceName);
        Assert.Contains(@"\DosDevices\E:", item.DisplayLocation);
    }

    [Theory]
    [InlineData("@usb.inf,%name%;USB Camera", "USB Camera")]
    [InlineData("@usb.inf,%name%", null)]
    [InlineData("  My Phone  ", "My Phone")]
    [InlineData("", null)]
    public void ResourceNames_AreHumanReadable(string value, string? expected) =>
        Assert.Equal(expected, DeviceIdentityResolver.ReadableName(value));

    [Fact]
    public void SharedLog_DoesNotInventDeviceName()
    {
        var item = new ArtifactItem { Category = ArtifactCategory.EventLogs, Type = ArtifactType.EventLog, Location = "System" };
        new DeviceIdentityResolver([]).Enrich(item);
        Assert.Equal("Общие записи — несколько устройств", item.DeviceName);
        Assert.Contains("весь журнал", item.CleanupEffect);
        Assert.Empty(item.RelatedDeviceIds);
    }

    [Fact]
    public void MissingInventory_UsesStorageModelWithoutClaimingConnectionState()
    {
        var item = Key(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk&Ven_Kingston&Prod_DataTraveler_3.0&Rev_1\ABC");
        new DeviceIdentityResolver([]).Enrich(item);
        Assert.Equal("Kingston DataTraveler 3.0", item.DeviceName);
        Assert.Null(item.DevicePresent);
    }

    [Fact]
    public void BasicScan_IsDefault_AndDoesNotCloseExplorer()
    {
        var options = new CleanupOptions();
        Assert.False(options.IncludeSharedArtifacts);
        Assert.False(options.CloseExplorer);
    }

    [Fact]
    public void OtherUsbSimulation_UsesOnlyReviewedPaths()
    {
        var result = OtherUsbTraceCleaner.Execute([
            new OtherUsbTraceItem { Vid = "FFFF", Pid = "1234", Selected = true, RegistryPaths = [@"SYSTEM\Reviewed"] },
            new OtherUsbTraceItem { Vid = "FFFF", Pid = "1234", Selected = false, RegistryPaths = [@"SYSTEM\NotSelected"] }
        ], simulation: true);
        Assert.True(result.Success);
        Assert.Equal(1, result.Processed);
        Assert.Contains("SYSTEM\\Reviewed", result.Log);
        Assert.DoesNotContain("NotSelected", result.Log);
    }

    [Fact]
    public void NetworkDefaults_AreReadOnly_AndDoNotTrustExampleNetworks()
    {
        var options = new NetworkAuditOptions();
        Assert.True(options.SimulationMode);
        Assert.False(options.ShowSecrets);
        Assert.False(options.DisconnectNetwork);
        Assert.False(options.RebootAfterClean);
        Assert.Empty(options.Whitelist.AllowedWiFi);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void FailedExternalCommand_IsNotReportedAsSuccess(int code, bool timeout) =>
        Assert.Throws<InvalidOperationException>(() => ProcessRunner.EnsureSuccess(new(code, "", "failure", timeout)));

    [Fact]
    public void NetworkSimulation_CountsPlannedAndProtectedRecords()
    {
        var items = new[] {
            new NetworkAuditItem { Kind = NetworkAuditKind.WiFiProfile, FilterGroup = NetworkAuditFilterGroup.WiFi,
                Title = "Chosen", Source = "test", Location = "chosen", Selected = true, CanClean = true },
            new NetworkAuditItem { Kind = NetworkAuditKind.WiFiProfile, FilterGroup = NetworkAuditFilterGroup.WiFi,
                Title = "Protected", Source = "test", Location = "protected", Selected = true, CanClean = true,
                AuthorizationStatus = NetworkAuthorizationStatus.Allowed }
        };
        var result = new NetworkAuditCleaner().Execute(items, new NetworkAuditOptions());
        Assert.Equal(1, result.Processed);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }
}
