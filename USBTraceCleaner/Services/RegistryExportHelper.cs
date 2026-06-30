namespace USBTraceCleaner.Services;

public static class RegistryExportHelper
{
    public static string GetExportRoot(string path)
    {
        if (path.Contains(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase))
        {
            var idx = path.IndexOf(@"\Enum\USBSTOR", StringComparison.OrdinalIgnoreCase);
            return path[..idx] + @"\Enum\USBSTOR";
        }
        if (path.Contains(@"\Enum\USBPRINT", StringComparison.OrdinalIgnoreCase))
        {
            var idx = path.IndexOf(@"\Enum\USBPRINT", StringComparison.OrdinalIgnoreCase);
            return path[..idx] + @"\Enum\USBPRINT";
        }
        if (path.Contains(@"\Enum\USB\", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(@"\Enum\USB", StringComparison.OrdinalIgnoreCase))
        {
            var idx = path.IndexOf(@"\Enum\USB", StringComparison.OrdinalIgnoreCase);
            return path[..idx] + @"\Enum\USB";
        }
        if (path.StartsWith(@"SYSTEM\MountedDevices", StringComparison.OrdinalIgnoreCase))
            return @"SYSTEM\MountedDevices";
        return path;
    }
}
