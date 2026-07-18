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
            // System — только в финальном Purge (иначе останется Event ID 104)
            if (channel.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                if (!cleanSystemLog)
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
            log?.Invoke("--- Полная очистка System (без остаточного Event ID 104) ---");
            var outcome = WindowsEventLogBrowser.PurgeSystemLogCompletely(log);
            if (outcome.Ok)
                log?.Invoke("  → System обнулён: остаточный Event ID 104 убран.");
            else
                log?.Invoke($"  ⚠ System: {outcome.Error}");
        }
        else if (!simulation && !cleanSystemLog)
        {
            log?.Invoke("  ⚠ После wevtutil cl в System обычно появляется Event ID 104 (признак очистки).");
        }
    }

    private static void ClearChannel(string channel, Action<string>? log)
    {
        var result = WindowsEventLogBrowser.ClearChannel(channel);
        if (result.Ok)
            log?.Invoke($"[OK]  LOG  {channel}");
        else if (result.WasSkipped)
            log?.Invoke($"[SKIP] канал отсутствует: {channel}");
        else
            log?.Invoke($"[WARN] LOG  {channel}: {result.Error}");
    }
}
