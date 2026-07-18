using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

public static class NetworkAuditHints
{
    public static string GetActionLabel(bool canClean) =>
        NetworkAuditDisplay.GetActionLabel(canClean);

    public static string GetCleanEffect(NetworkAuditKind kind, string title) =>
        NetworkAuditDisplay.GetCleanEffect(kind, title);

    public const string HelpText =
        """
        РАЗДЕЛЫ (слева):
        • Wi‑Fi — сохранённые сети, пароли, события подключения
        • Ethernet — адаптеры, кабель, DHCP, профили сети
        • VPN — профили и события VPN
        • Роутер — шлюз, устройства в LAN (данные роутера, не на диске ПК)
        • DNS — кэш имён сайтов
        • Журналы — события Windows за выбранный период
        • Реестр — профили сетей, hosts, VPN, SRU
        • Кэши — NLA, NetBIOS, SRU

        КОЛОНКИ:
        • Разрешено? — подключение из белого списка; такие элементы не удаляются при очистке
        • Тип — «Удалить» или «Просмотр» (роутер, адаптер, отдельные события)

        МАКСИМАЛЬНАЯ ОЧИСТКА:
        • Удаляет следы на ПК вне белого списка: профили, журналы, SRU, реестр, DNS, NetBIOS
        • Wi‑Fi/VPN/IP из белого списка сохраняются
        • hosts — только если вы подтвердите (Docker перестанет резолвиться)
        • Отключает сеть и перезагружает ПК
        """;

    public const string HostsWarning =
        """
        Очистить файл hosts?

        ПОСЛЕДСТВИЯ:
        • Записи Docker (host.docker.internal, kubernetes.docker.internal) УДАЛЯТСЯ
        • Локальные домены и редиректы перестанут работать
        • Docker Desktop / Kubernetes на этом ПК могут потребовать перезапуск
        • Закройте Docker Desktop и Kaspersky ПЕРЕД очисткой — иначе записи вернутся или доступ будет запрещён

        Останется только стандартный Windows: 127.0.0.1 localhost

        Очистить hosts?
        """;

    public static string BuildCleanupWarning(IEnumerable<NetworkAuditItem> items, bool fullClean)
    {
        var selected = fullClean
            ? items.Where(i => i.CanClean).ToList()
            : items.Where(i => i.Selected && i.CanClean).ToList();

        var unknown = selected.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown);
        var allowed = selected.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Allowed);
        var wifi = selected.Count(i => i.Kind == NetworkAuditKind.WiFiProfile);
        var logs = selected.Count(i => i.Kind == NetworkAuditKind.EventLogChannel);
        var vpn = selected.Count(i => i.Kind == NetworkAuditKind.VpnProfile || i.Kind == NetworkAuditKind.RegistryTrace);
        var registry = selected.Count(i => i.Kind is NetworkAuditKind.NetworkProfile or NetworkAuditKind.NlaCache);
        var dns = selected.Any(i => i.Kind == NetworkAuditKind.DnsCache && i.Location == "__flushdns__");
        var sru = selected.Any(i => i.Kind == NetworkAuditKind.SruDatabase);

        var mode = fullClean ? "МАКСИМАЛЬНАЯ ОЧИСТКА" : "Выборочная очистка";

        var toClean = selected.Count(i => i.AuthorizationStatus != NetworkAuthorizationStatus.Allowed);

        return
            $"{mode}\nК очистке (вне белого списка): {toClean}\n" +
            $"Сохраняются (белый список): {allowed} | Неизвестных к удалению: {unknown}\n\n" +
            $"• Wi‑Fi профили: {wifi}\n" +
            $"• VPN / подписи: {vpn}\n" +
            $"• Журналы (каналы): {logs}\n" +
            $"• Реестр сети: {registry}\n" +
            $"• DNS-кэш: {(dns ? "да" : "нет")}\n" +
            $"• SRU (история трафика): {(sru ? "да" : "нет")}\n\n" +
            (fullClean
                ? "Элементы белого списка не удаляются.\n" +
                  "Закройте HAPP/VPN и Docker Desktop перед очисткой.\n" +
                  "Wi‑Fi сессия будет разорвана (адаптер останется включённым). Windows перезагрузится.\n\n"
                : "Элементы белого списка не удаляются.\n\n") +
            "Продолжить?";
    }
}
