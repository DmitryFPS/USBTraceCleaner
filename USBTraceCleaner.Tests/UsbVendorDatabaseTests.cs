using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class UsbVendorDatabaseTests
{
    [Fact]
    public void Database_LoadsFromEmbeddedResource()
    {
        Assert.True(UsbVendorDatabase.VendorCount > 1000);
        Assert.True(UsbVendorDatabase.ProductCount > 5000);
    }

    [Fact]
    public void LookupVendor_FindsVmware()
    {
        Assert.Equal("VMware, Inc.", UsbVendorDatabase.LookupVendor("0E0F"));
        Assert.Equal("VMware, Inc.", UsbVendorDatabase.LookupVendor("0e0f"));
    }

    [Fact]
    public void LookupProduct_FindsVmwareHubAndMouse()
    {
        Assert.Equal("Virtual USB Hub", UsbVendorDatabase.LookupProduct("0E0F", "0002"));
        Assert.Equal("Virtual Mouse", UsbVendorDatabase.LookupProduct("0e0f", "0003"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LookupVendor_ReturnsNullForEmpty(string? vid)
    {
        Assert.Null(UsbVendorDatabase.LookupVendor(vid));
    }

    [Fact]
    public void LookupProduct_ReturnsNullForUnknown()
    {
        Assert.Null(UsbVendorDatabase.LookupProduct("FFFF", "FFFF"));
    }
}
