using System.Diagnostics.Eventing.Reader;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal static class EventLogReaderHelper
{
    internal static readonly string[] NetworkChannels =
    [
        "Microsoft-Windows-WLAN-AutoConfig/Operational",
        "Microsoft-Windows-WLAN-Driver/Operational",
        "Microsoft-Windows-NetworkProfile/Operational",
        "Microsoft-Windows-Dhcp-Client/Operational",
        "Microsoft-Windows-DNS-Client/Operational",
        "Microsoft-Windows-NCSI/Operational",
        "Microsoft-Windows-RemoteAccess/Operational",
        "Microsoft-Windows-RRAS/Operational",
        "Microsoft-Windows-WinINet/Operational",
        "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall",
        "Microsoft-Windows-NlaSvc/Operational",
        "Microsoft-Windows-NDIS/Operational",
        "System"
    ];

    public static IEnumerable<NetworkAuditItem> ReadEvents(
        string channel,
        DateTime from,
        DateTime to,
        NetworkAuditFilterGroup group,
        NetworkAuditKind kind)
    {
        if (!EventLogChannelHelper.Exists(channel))
            yield break;

        var items = new List<NetworkAuditItem>();
        try
        {
            var query = new EventLogQuery(channel, PathType.LogName) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    var time = record.TimeCreated;
                    if (time == null) continue;
                    if (time < from) break;
                    if (time > to) continue;

                    var desc = NetworkAuditDisplay.SanitizeForDisplay(record.FormatDescription() ?? record.Id.ToString());
                    if (desc.Length > 500) desc = desc[..500] + "…";

                    items.Add(new NetworkAuditItem
                    {
                        EventTime = record.TimeCreated,
                        Kind = kind,
                        FilterGroup = group,
                        Source = channel,
                        Title = $"Событие {record.Id}: {record.LevelDisplayName}",
                        Detail = desc,
                        Location = channel,
                        CanClean = false
                    });
                }
            }
        }
        catch
        {
            // Канал есть, но чтение не удалось — не предлагаем ложную очистку
        }

        foreach (var item in items)
            yield return item;
    }

    public static NetworkAuditItem? TryCreateCleanupItem(string channel, NetworkAuditFilterGroup group)
    {
        if (!EventLogChannelHelper.Exists(channel))
            return null;

        return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.EventLogChannel,
            FilterGroup = group,
            Source = "wevtutil",
            Title = $"Очистить журнал: {channel}",
            Detail = "Полная очистка канала событий",
            Location = channel,
            CanClean = true
        };
    }
}
