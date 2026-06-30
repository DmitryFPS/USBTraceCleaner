using System.IO;

namespace USBTraceCleaner.Services;

public static class AppPaths
{
    public static string GetExeDirectory()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
            return Path.GetDirectoryName(exePath)!;

        return AppContext.BaseDirectory.TrimEnd('\\', '/');
    }
}
