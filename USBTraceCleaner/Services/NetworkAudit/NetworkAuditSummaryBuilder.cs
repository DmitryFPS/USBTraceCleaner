using System.Text.RegularExpressions;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

public static class NetworkAuditSummaryBuilder
{
    private static readonly Regex LastConnectedRegex = new(
        @"Последнее:\s*(.+?)(?:\s*\||$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<NetworkAuditKind> ConnectionEventKinds =
    [
        NetworkAuditKind.WiFiEvent,
        NetworkAuditKind.VpnEvent,
        NetworkAuditKind.EthernetEvent,
        NetworkAuditKind.DhcpEvent,
        NetworkAuditKind.NetworkProfile
    ];

    public static NetworkAuditReadableSummary Build(
        IEnumerable<NetworkAuditItem> items,
        NetworkAuditWhitelist? whitelist = null)
    {
        var list = items.ToList();
        whitelist ??= new NetworkAuditWhitelist();

        var allowed = list.Where(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Allowed).ToList();
        var unknown = list.Where(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown).ToList();

        var sections = new List<NetworkAuditSummarySection>
        {
            BuildOverview(list, allowed, unknown, whitelist)
        };

        var yours = BuildYourConnections(allowed);
        if (yours.Lines.Count > 0)
            sections.Add(yours);

        var check = BuildNeedsReview(unknown);
        if (check.Lines.Count > 0)
            sections.Add(check);

        var current = BuildCurrentState(list);
        if (current.Lines.Count > 0)
            sections.Add(current);

        var events = BuildRecentEvents(list);
        if (events.Lines.Count > 0)
            sections.Add(events);

        sections.Add(BuildCleanupNote(list));

        return new NetworkAuditReadableSummary { Sections = sections };
    }

    private static NetworkAuditSummarySection BuildOverview(
        IReadOnlyList<NetworkAuditItem> all,
        IReadOnlyList<NetworkAuditItem> allowed,
        IReadOnlyList<NetworkAuditItem> unknown,
        NetworkAuditWhitelist whitelist)
    {
        var wifiProfiles = all.Count(i => i.Kind == NetworkAuditKind.WiFiProfile);
        var unknownWifi = unknown.Count(i => i.Kind == NetworkAuditKind.WiFiProfile);
        var unknownVpn = unknown.Count(i => i.Kind == NetworkAuditKind.VpnProfile);

        var whitelistText = FormatWhitelist(whitelist);
        var lines = new List<string>
        {
            $"Всего записей в таблице: {all.Count} (это следы на ПК + текущее состояние).",
            $"В белом списке: {allowed.Count} | Не в списке: {unknown.Count}.",
            $"Сохранённые Wi‑Fi сети: {wifiProfiles} (не в списке: {unknownWifi}).",
        };

        if (unknownWifi > 0 || unknownVpn > 0)
            lines.Add($"На проверку: чужих Wi‑Fi — {unknownWifi}, VPN — {unknownVpn}.");

        lines.Add($"Белый список: {whitelistText}.");

        return new NetworkAuditSummarySection
        {
            Title = "Краткий итог",
            Lines = lines
        };
    }

    private static NetworkAuditSummarySection BuildYourConnections(IReadOnlyList<NetworkAuditItem> allowed)
    {
        var lines = new List<string>();

        foreach (var item in allowed.Where(i => i.Kind == NetworkAuditKind.WiFiProfile).OrderBy(i => i.Title))
            lines.Add(FormatWiFiProfile(item, inWhitelist: true));

        foreach (var item in allowed.Where(i => i.Kind == NetworkAuditKind.VpnProfile).OrderBy(i => i.Title))
            lines.Add(FormatVpnProfile(item, inWhitelist: true));

        foreach (var item in allowed.Where(i => i.Kind is NetworkAuditKind.RouterDevice or NetworkAuditKind.ArpEntry
                                               or NetworkAuditKind.RouterDhcp).OrderBy(i => i.Title))
            lines.Add(FormatRouterDevice(item, inWhitelist: true));

        if (lines.Count == 0)
            lines.Add("Совпадений с белым списком не найдено — проверьте IP, Wi‑Fi и VPN в настройках.");

        return new NetworkAuditSummarySection
        {
            Title = "Ваши подключения (в белом списке)",
            Hint = "Это то, что вы сами указали как «своё»",
            Lines = lines
        };
    }

    private static NetworkAuditSummarySection BuildNeedsReview(IReadOnlyList<NetworkAuditItem> unknown)
    {
        var lines = new List<string>();

        foreach (var item in unknown.Where(i => i.Kind == NetworkAuditKind.WiFiProfile).OrderBy(i => i.Title))
            lines.Add(FormatWiFiProfile(item, inWhitelist: false));

        foreach (var item in unknown.Where(i => i.Kind == NetworkAuditKind.VpnProfile).OrderBy(i => i.Title))
            lines.Add(FormatVpnProfile(item, inWhitelist: false));

        foreach (var item in unknown.Where(i => i.Kind is NetworkAuditKind.RouterDevice or NetworkAuditKind.ArpEntry
                                               or NetworkAuditKind.RouterDhcp).OrderBy(i => i.Title).Take(12))
            lines.Add(FormatRouterDevice(item, inWhitelist: false));

        var dnsLines = unknown
            .Where(i => i.Kind == NetworkAuditKind.DnsCache && i.Location != "__flushdns__")
            .Select(i => i.Title.Replace("DNS: ", "", StringComparison.Ordinal))
            .Where(n => !IsBoringDns(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(n => $"DNS: недавно запрашивался «{n}»")
            .ToList();
        lines.AddRange(dnsLines);

        if (lines.Count == 0)
            lines.Add("Чужих Wi‑Fi, VPN и незнакомых устройств не найдено.");

        return new NetworkAuditSummarySection
        {
            Title = "Не в белом списке — проверьте",
            Hint = "Если не узнаёте — возможно, чужое подключение или след на ПК",
            Lines = lines,
            IsAttention = unknown.Any(i => i.Kind is NetworkAuditKind.WiFiProfile or NetworkAuditKind.VpnProfile)
        };
    }

    private static NetworkAuditSummarySection BuildCurrentState(IReadOnlyList<NetworkAuditItem> all)
    {
        var lines = all
            .Where(i => i.Kind == NetworkAuditKind.EthernetAdapter)
            .Select(i => $"{i.Title} — {ShortDetail(i.Detail)}")
            .ToList();

        var gateway = all.FirstOrDefault(i => i.Kind == NetworkAuditKind.RouterGateway);
        if (gateway != null)
            lines.Insert(0, $"{gateway.Title} ({gateway.Detail})");

        return new NetworkAuditSummarySection
        {
            Title = "Сейчас на этом ПК",
            Hint = "Текущие адаптеры и шлюз — не удаляются, только для справки",
            Lines = lines
        };
    }

    private static NetworkAuditSummarySection BuildRecentEvents(IReadOnlyList<NetworkAuditItem> all)
    {
        var lines = all
            .Where(i => ConnectionEventKinds.Contains(i.Kind) && i.EventTime.HasValue)
            .OrderByDescending(i => i.EventTime)
            .Take(10)
            .Select(i =>
            {
                var mark = i.AuthorizationStatus == NetworkAuthorizationStatus.Allowed ? "✓" : "?";
                var when = i.EventTime!.Value.ToString("dd.MM.yyyy HH:mm");
                var text = ShortDetail(i.Detail);
                if (string.IsNullOrWhiteSpace(text))
                    text = i.Title;
                return $"[{mark}] {when} — {text}";
            })
            .ToList();

        return new NetworkAuditSummarySection
        {
            Title = "Последние события подключения",
            Hint = "✓ — в белом списке, ? — не в списке. Полный список — в разделе «Журналы»",
            Lines = lines
        };
    }

    private static NetworkAuditSummarySection BuildCleanupNote(IReadOnlyList<NetworkAuditItem> all)
    {
        var cleanable = all.Count(i => i.CanClean);
        var wifi = all.Count(i => i.Kind == NetworkAuditKind.WiFiProfile && i.CanClean);
        var logs = all.Count(i => i.Kind == NetworkAuditKind.EventLogChannel);
        var registry = all.Count(i => i.FilterGroup == NetworkAuditFilterGroup.Registry && i.CanClean);
        var caches = all.Count(i => i.FilterGroup == NetworkAuditFilterGroup.Cache && i.CanClean);

        var lines = new List<string>
        {
            $"При очистке можно обработать на ПК: {cleanable} записей.",
            $"Wi‑Fi профили: {wifi}, журналы Windows: {logs}, реестр: {registry}, кэши: {caches}.",
            "Остальное в таблице — служебные следы (журналы целиком, SRU, NLA и т.д.)."
        };

        return new NetworkAuditSummarySection
        {
            Title = "Если нажмёте «Очистить»",
            Hint = "«Разрешено» не защищает от удаления — при макс. очистке стирается всё на диске",
            Lines = lines
        };
    }

    private static string FormatWiFiProfile(NetworkAuditItem item, bool inWhitelist)
    {
        var name = ExtractAfterPrefix(item.Title, "Профиль Wi‑Fi: ");
        var last = MatchLastConnected(item.Detail);
        var status = inWhitelist ? "ваша сеть" : "НЕ в белом списке";
        return string.IsNullOrWhiteSpace(last)
            ? $"Wi‑Fi «{name}» — {status}"
            : $"Wi‑Fi «{name}» — {status}, последнее: {last}";
    }

    private static string FormatVpnProfile(NetworkAuditItem item, bool inWhitelist)
    {
        var status = inWhitelist ? "ваш VPN" : "НЕ в белом списке";
        if (item.Title.StartsWith("VPN-подключение:", StringComparison.Ordinal))
        {
            var name = ExtractAfterPrefix(item.Title, "VPN-подключение: ");
            return $"VPN «{name}» — {status}";
        }

        return $"{item.Title} — {status}";
    }

    private static string FormatRouterDevice(NetworkAuditItem item, bool inWhitelist)
    {
        var status = inWhitelist ? "ваш IP" : "не в списке IP";
        return $"{item.Title} ({item.Detail}) — {status}";
    }

    private static string FormatWhitelist(NetworkAuditWhitelist whitelist)
    {
        var parts = new List<string>();
        if (whitelist.AllowedIps.Count > 0)
            parts.Add($"IP: {string.Join(", ", whitelist.AllowedIps)}");
        if (whitelist.AllowedWiFi.Count > 0)
            parts.Add($"Wi‑Fi: {string.Join(", ", whitelist.AllowedWiFi)}");
        if (whitelist.AllowedVpn.Count > 0)
            parts.Add($"VPN: {string.Join(", ", whitelist.AllowedVpn)}");
        return parts.Count > 0 ? string.Join("; ", parts) : "не задан";
    }

    private static string ExtractAfterPrefix(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal)
            ? text[prefix.Length..].Trim()
            : text.Trim();

    private static string? MatchLastConnected(string detail)
    {
        var m = LastConnectedRegex.Match(detail);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string ShortDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "";
        detail = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return detail.Length <= 120 ? detail : detail[..120] + "…";
    }

    private static bool IsBoringDns(string name) =>
        name.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("wpad", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("msftncsi", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("msftconnecttest", StringComparison.OrdinalIgnoreCase);
}
