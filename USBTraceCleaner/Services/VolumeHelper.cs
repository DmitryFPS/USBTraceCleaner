using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class VolumeHelper
{
    public static void OfflineUsbDisks(Action<string>? log = null)
    {
        const string script = """
            Get-Disk | Where-Object { $_.BusType -eq 'USB' } | ForEach-Object {
                try {
                    Set-Disk -Number $_.Number -IsOffline $true -ErrorAction Stop
                    Write-Output "OFFLINE disk $($_.Number)"
                } catch {
                    Write-Output "SKIP disk $($_.Number)"
                }
            }
            """;

        var result = ProcessExec.Run(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", script],
            15_000);

        if (result.TimedOut)
        {
            log?.Invoke("  [WARN] Offline USB disks: таймаут powershell");
            return;
        }

        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            log?.Invoke($"  {line.Trim()}");
    }

    public static bool HasMountedUsbVolumes()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Removable) continue;
            try
            {
                if (drive.IsReady) return true;
            }
            catch { return true; }
        }
        return false;
    }
}
