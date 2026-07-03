using System.Reflection;

namespace USBTraceCleaner.Services;

public static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static string VersionLabel => $"v{Version}";
}
