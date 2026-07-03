using System.Text;

namespace USBTraceCleaner.Models;

public static class NetworkAuditDisplay
{
    public static string CollapseDetail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var parts = text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" | ", parts);
    }

    public static string SanitizeForDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\uFFFD')
                continue;
            if (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t'))
                continue;
            if (char.IsSurrogate(ch))
                continue;
            sb.Append(ch);
        }

        return CollapseDetail(sb.ToString());
    }

    public static string GetActionLabel(bool canClean) =>
        canClean ? "Удалить" : "Просмотр";

    public static string GetCleanEffect(NetworkAuditKind kind, string title)
    {
        if (title.Contains("Очистить", StringComparison.OrdinalIgnoreCase))
        {
            if (title.Contains("DNS", StringComparison.OrdinalIgnoreCase))
                return "Полная очистка DNS-кэша Windows";
            if (title.Contains("журнал", StringComparison.OrdinalIgnoreCase))
                return "Полная очистка журнала событий";
            if (title.Contains("NetBIOS", StringComparison.OrdinalIgnoreCase))
                return "Сброс NetBIOS-кэша";
            return "Удаление выбранного следа";
        }

        return kind switch
        {
            NetworkAuditKind.WiFiProfile => "Профиль Wi‑Fi исчезнет; пароль нужно ввести заново",
            NetworkAuditKind.DnsCache => "Сброс DNS-кэша",
            NetworkAuditKind.EventLogChannel => "Журнал событий будет полностью очищен",
            NetworkAuditKind.NetworkProfile => "Windows «забудет» тип сети для этого профиля",
            NetworkAuditKind.NlaCache => "Удалится кэш имён сетей NLA",
            NetworkAuditKind.VpnProfile => "VPN пропадёт из списка; настройки заново",
            NetworkAuditKind.NetbiosCache => "Сброс NetBIOS-кэша",
            NetworkAuditKind.SruDatabase => "Удаление истории трафика приложений (SRU)",
            NetworkAuditKind.HostsFile => "Сброс файла hosts до стандартного",
            NetworkAuditKind.RegistryTrace => "Удаление подписи VPN/сети из реестра",
            NetworkAuditKind.WlanRegistry => "Удаление следов WLAN-интерфейса в реестре",
            _ => "—"
        };
    }
}
