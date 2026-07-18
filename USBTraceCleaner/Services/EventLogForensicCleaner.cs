using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class EventLogForensicCleaner
{
    /// <summary>
    /// Дополнительные каналы, где UsbForensicAudit находит USBSTOR/UserPnp.
    /// Полная очистка System убирает и Event ID 104 от предыдущих clear.
    /// </summary>
    public static readonly string[] ExtraChannels =
    [
        "Microsoft-Windows-UserPnp/Operational",
        "Microsoft-Windows-Kernel-PnP/Device Management",
        "Microsoft-Windows-Kernel-PnP/Device Configuration",
        "System",
    ];

    public static void ClearExtraChannels(bool cleanSystemLog, bool simulation, Action<string>? log = null)
    {
        foreach (var channel in ExtraChannels)
        {
            if (channel.Equals("System", StringComparison.OrdinalIgnoreCase) && !cleanSystemLog)
            {
                log?.Invoke("[SKIP] System event log (CleanSystemEventLog=false). UserPnp/USBSTOR в System могут остаться.");
                continue;
            }

            if (simulation)
            {
                log?.Invoke($"[SIM] LOG  {channel}");
                continue;
            }

            ClearChannel(channel, log);
        }

        if (!simulation && cleanSystemLog)
        {
            log?.Invoke("  → System очищен: Event ID 104 от предыдущих wevtutil cl также удалены.");
            log?.Invoke("  → Новая очистка каналов может снова создать 104 — это ограничение Windows.");
        }
        else if (!simulation)
        {
            log?.Invoke("  ⚠ После wevtutil cl в System обычно появляется Event ID 104 (признак очистки).");
        }
    }

    private static void ClearChannel(string channel, Action<string>? log)
    {
        try
        {
            var psi = new ProcessStartInfo("wevtutil.exe", $"cl \"{channel}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                log?.Invoke($"[FAIL] LOG  {channel}");
                return;
            }

            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            if (proc.ExitCode == 0)
                log?.Invoke($"[OK]  LOG  {channel}");
            else if (stderr.Contains("cannot be found", StringComparison.OrdinalIgnoreCase)
                     || stderr.Contains("не найден", StringComparison.OrdinalIgnoreCase))
                log?.Invoke($"[SKIP] канал отсутствует: {channel}");
            else
                log?.Invoke($"[WARN] LOG  {channel} exit={proc.ExitCode}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[ERR] LOG  {channel}: {ex.Message}");
        }
    }
}
