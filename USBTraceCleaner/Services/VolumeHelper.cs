using System.Diagnostics;
using System.IO;
using System.Diagnostics.CodeAnalysis;

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

        var psi = new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return;
        proc.WaitForExit(15000);
        var output = proc.StandardOutput.ReadToEnd();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
