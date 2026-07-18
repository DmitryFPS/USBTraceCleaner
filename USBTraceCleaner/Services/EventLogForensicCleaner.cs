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
            // Ещё раз в самом конце — 104 от очистки UserPnp/Kernel-PnP выше
            log?.Invoke("--- Повторная очистка System (Event ID 104) ---");
            ClearChannel("System", log);
            log?.Invoke("  → System очищен в конце: записи Event ID 104 убраны.");
        }
        else if (!simulation)
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
