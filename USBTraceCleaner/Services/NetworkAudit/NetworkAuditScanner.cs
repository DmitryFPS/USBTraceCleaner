using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
public sealed class NetworkAuditScanner
{
    private readonly RouterAuditScanner _router = new();

    public IReadOnlyList<NetworkAuditItem> Scan(
        NetworkAuditOptions options,
        IProgress<NetworkAuditProgress>? progress = null)
    {
        var items = new List<NetworkAuditItem>();

        void AddPhase(string phase, IEnumerable<NetworkAuditItem> batch)
        {
            progress?.Report(new NetworkAuditProgress { Phase = phase });
            items.AddRange(batch);
            progress?.Report(new NetworkAuditProgress { Phase = phase, ItemsFound = items.Count });
        }

        if (options.ScanWiFi)
        {
            AddPhase("Wi‑Fi: профили netsh…", NetshHelper.ScanWiFiProfiles());
            AddPhase("Wi‑Fi: XML профили…", WindowsNetworkScanner.ScanWlansvcFiles());
        }

        if (options.ScanEthernet)
        {
            AddPhase("Сетевые адаптеры…", NetshHelper.ScanInterfaces());
        }

        if (options.ScanDns)
            AddPhase("DNS-кэш…", WindowsNetworkScanner.ScanDns());

        if (options.ScanRegistry)
            AddPhase("Реестр сети…", WindowsNetworkScanner.ScanRegistry());
        else if (options.ScanVpn)
            AddPhase("VPN…", WindowsNetworkScanner.ScanVpnOnly());

        if (options.ScanEventLogs)
            AddPhase("Журналы событий…", WindowsNetworkScanner.ScanEventLogs(options));

        if (options.ScanCaches)
        {
            AddPhase("NetBIOS кэш…", WindowsNetworkScanner.ScanNetbiosCache());
            AddPhase("Брандмауэр…", WindowsNetworkScanner.ScanFirewallRules());
        }

        if (options.ScanUsbBluetooth)
            AddPhase("USB/BT сеть…", WindowsNetworkScanner.ScanUsbBluetoothNetwork());

        if (options.ScanRouter)
            AddPhase("Роутер и LAN…", _router.Scan(options));

        var deduped = items
            .GroupBy(i => $"{i.Kind}|{i.Location}|{i.Title}")
            .Select(g => g.First())
            .ToList();

        return FinalizeScan(deduped, options);
    }

    public static IReadOnlyList<NetworkAuditItem> FinalizeScan(
        IReadOnlyList<NetworkAuditItem> items,
        NetworkAuditOptions options)
    {
        NetworkAuditClassifier.ApplyWhitelist(items, options.Whitelist);
        return items
            .OrderByDescending(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown)
            .ThenByDescending(i => i.EventTime ?? DateTime.MinValue)
            .ThenBy(i => i.DisplayGroup)
            .ThenBy(i => i.Title)
            .ToList();
    }
}
