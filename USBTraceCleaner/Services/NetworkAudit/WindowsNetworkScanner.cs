using System.IO;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

internal static class WindowsNetworkScanner
{
    public static IEnumerable<NetworkAuditItem> ScanDns()
    {
        var output = ProcessRunner.Run("ipconfig", "/displaydns");
        foreach (var block in output.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!block.Contains("Record Name", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Match(block, @"Record Name\s*\.+\s*(.+)");
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.DnsCache,
                FilterGroup = NetworkAuditFilterGroup.Dns,
                Source = "DNS-кэш",
                Title = $"DNS: {name.Trim()}",
                Detail = Truncate(block.Replace('\r', ' ').Replace('\n', ' '), 400),
                Location = name.Trim(),
                CanClean = true
            };
        }

        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.DnsCache,
            FilterGroup = NetworkAuditFilterGroup.Dns,
            Source = "ipconfig",
            Title = "Очистить DNS-кэш (все записи)",
            Detail = "ipconfig /flushdns",
            Location = "__flushdns__",
            CanClean = true
        };
    }

    public static IEnumerable<NetworkAuditItem> ScanVpnOnly() => ScanVpnProfiles();

    public static IEnumerable<NetworkAuditItem> ScanRegistry()
    {
        foreach (var item in ScanRegistryKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles",
                     NetworkAuditKind.NetworkProfile, NetworkAuditFilterGroup.Registry, "Профиль сети", true))
            yield return item;

        foreach (var item in ScanRegistryKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Unmanaged",
                     NetworkAuditKind.RegistryTrace, NetworkAuditFilterGroup.Registry, "Подпись сети", false))
            yield return item;

        foreach (var item in ScanRegistryKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Nla\Cache",
                     NetworkAuditKind.NlaCache, NetworkAuditFilterGroup.Cache, "NLA cache", true))
            yield return item;

        foreach (var item in ScanWlanInterfaces())
            yield return item;

        foreach (var item in ScanVpnProfiles())
            yield return item;

        foreach (var item in ScanHostsFile())
            yield return item;

        foreach (var item in ScanSru())
            yield return item;
    }

    private static IEnumerable<NetworkAuditItem> ScanRegistryKey(
        string path, NetworkAuditKind kind, NetworkAuditFilterGroup group, string label, bool canClean)
    {
        var items = new List<NetworkAuditItem>();
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var sub in key.GetSubKeyNames())
            {
                using var profile = key.OpenSubKey(sub);
                var name = profile?.GetValue("ProfileName")?.ToString() ?? sub;
                var desc = profile?.GetValue("Description")?.ToString() ?? "";
                items.Add(new NetworkAuditItem
                {
                    Kind = kind,
                    FilterGroup = group,
                    Source = path,
                    Title = $"{label}: {name}",
                    Detail = string.IsNullOrWhiteSpace(desc) ? $"GUID: {sub}" : desc,
                    Location = sub,
                    CanClean = canClean
                });
            }
        }, RegistryHive.LocalMachine, path);
        return items;
    }

    private static IEnumerable<NetworkAuditItem> ScanWlanInterfaces()
    {
        var items = new List<NetworkAuditItem>();
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var sub in key.GetSubKeyNames())
            {
                using var iface = key.OpenSubKey(sub);
                var desc = iface?.GetValue("Description")?.ToString() ?? sub;
                items.Add(new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.WlanRegistry,
                    FilterGroup = NetworkAuditFilterGroup.WiFi,
                    Source = @"HKLM\WlanSvc\Interfaces",
                    Title = $"WLAN интерфейс: {desc}",
                    Detail = $"GUID: {sub}",
                    Location = sub,
                    CanClean = false
                });
            }
        }, RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\WlanSvc\Interfaces");
        return items;
    }

    private static IEnumerable<NetworkAuditItem> ScanVpnProfiles()
    {
        var items = new List<NetworkAuditItem>();
        var pbk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Network\Connections\Pbk\rasphone.pbk");
        if (File.Exists(pbk))
        {
            items.Add(new NetworkAuditItem
            {
                Kind = NetworkAuditKind.VpnProfile,
                FilterGroup = NetworkAuditFilterGroup.Vpn,
                Source = "rasphone.pbk",
                Title = "Файл VPN-профилей Windows",
                Detail = pbk,
                Location = pbk,
                CanClean = true
            });
        }

        RegistryHelper.SafeOpen(key =>
        {
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                items.Add(new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.VpnProfile,
                    FilterGroup = NetworkAuditFilterGroup.Vpn,
                    Source = "Internet Settings\\Connections",
                    Title = $"VPN-подключение: {name}",
                    Detail = sub?.GetValue("PhoneNumber")?.ToString() ?? "—",
                    Location = name,
                    CanClean = true
                });
            }
        }, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections");

        return items;
    }

    private static IEnumerable<NetworkAuditItem> ScanHostsFile()
    {
        var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
        if (!File.Exists(hosts)) yield break;
        var lines = File.ReadAllLines(hosts).Where(l => !l.TrimStart().StartsWith('#') && l.Trim().Length > 0).ToList();
        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.HostsFile,
            FilterGroup = NetworkAuditFilterGroup.Registry,
            Source = "hosts",
            Title = $"Файл hosts ({lines.Count} записей)",
            Detail = Truncate(string.Join(" | ", lines), 500),
            Location = hosts,
            CanClean = false
        };
    }

    private static IEnumerable<NetworkAuditItem> ScanSru()
    {
        var sru = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"sru\SRUDB.dat");
        if (!File.Exists(sru)) yield break;
        var info = new FileInfo(sru);
        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.SruDatabase,
            FilterGroup = NetworkAuditFilterGroup.Cache,
            Source = "SRUDB.dat",
            Title = "История сетевой активности приложений (SRU)",
            Detail = $"Размер: {info.Length / 1024} KB | Изменён: {info.LastWriteTime:dd.MM.yyyy HH:mm}",
            Location = sru,
            CanClean = false
        };
    }

    public static IEnumerable<NetworkAuditItem> ScanUsbBluetoothNetwork()
    {
        var items = new List<NetworkAuditItem>();
        RegistryHelper.SafeOpen(key =>
        {
            foreach (var sub in key.GetSubKeyNames())
            {
                if (!sub.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) continue;
                using var dev = key.OpenSubKey(sub);
                var cls = dev?.GetValue("Class")?.ToString() ?? "";
                if (!cls.Contains("Net", StringComparison.OrdinalIgnoreCase) &&
                    !cls.Contains("Modem", StringComparison.OrdinalIgnoreCase))
                    continue;
                items.Add(new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.UsbNetwork,
                    FilterGroup = NetworkAuditFilterGroup.UsbBluetooth,
                    Source = @"Enum\USB",
                    Title = $"USB-сетевое устройство: {sub}",
                    Detail = $"Class: {cls}",
                    Location = sub,
                    CanClean = false
                });
            }
        }, RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Enum\USB");
        return items;
    }

    public static IEnumerable<NetworkAuditItem> ScanEventLogs(NetworkAuditOptions options)
    {
        var map = new (string channel, NetworkAuditFilterGroup group, NetworkAuditKind kind)[]
        {
            ("Microsoft-Windows-WLAN-AutoConfig/Operational", NetworkAuditFilterGroup.WiFi, NetworkAuditKind.WiFiEvent),
            ("Microsoft-Windows-WLAN-Driver/Operational", NetworkAuditFilterGroup.WiFi, NetworkAuditKind.WiFiEvent),
            ("Microsoft-Windows-NetworkProfile/Operational", NetworkAuditFilterGroup.Ethernet, NetworkAuditKind.EthernetEvent),
            ("Microsoft-Windows-Dhcp-Client/Operational", NetworkAuditFilterGroup.Ethernet, NetworkAuditKind.DhcpEvent),
            ("Microsoft-Windows-DNS-Client/Operational", NetworkAuditFilterGroup.Dns, NetworkAuditKind.DnsEvent),
            ("Microsoft-Windows-RemoteAccess/Operational", NetworkAuditFilterGroup.Vpn, NetworkAuditKind.VpnEvent),
            ("Microsoft-Windows-RRAS/Operational", NetworkAuditFilterGroup.Vpn, NetworkAuditKind.VpnEvent),
            ("Microsoft-Windows-NCSI/Operational", NetworkAuditFilterGroup.Other, NetworkAuditKind.NcsiEvent),
            ("Microsoft-Windows-WinINet/Operational", NetworkAuditFilterGroup.Other, NetworkAuditKind.WinInetTrace),
            ("Microsoft-Windows-NlaSvc/Operational", NetworkAuditFilterGroup.Cache, NetworkAuditKind.NlaCache),
            ("Microsoft-Windows-NDIS/Operational", NetworkAuditFilterGroup.Ethernet, NetworkAuditKind.EthernetEvent),
            ("Microsoft-Windows-Windows Firewall With Advanced Security/Firewall", NetworkAuditFilterGroup.Other, NetworkAuditKind.FirewallEvent)
        };

        foreach (var (channel, group, kind) in map)
        {
            foreach (var item in EventLogReaderHelper.ReadEvents(channel, options.DateFrom, options.DateTo, group, kind))
                yield return item;

            yield return EventLogReaderHelper.ChannelCleanupItem(channel, group);
        }
    }

    public static IEnumerable<NetworkAuditItem> ScanFirewallRules()
    {
        var output = ProcessRunner.Run("netsh", "advfirewall firewall show rule name=all");
        var count = output.Split('\n').Count(l => l.StartsWith("Rule Name", StringComparison.OrdinalIgnoreCase));
        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.FirewallRule,
            FilterGroup = NetworkAuditFilterGroup.Other,
            Source = "netsh advfirewall",
            Title = $"Правила брандмауэра Windows: {count}",
            Detail = "Список сетевых правил (аудит, без удаления)",
            Location = "Firewall",
            CanClean = false
        };
    }

    public static IEnumerable<NetworkAuditItem> ScanNetbiosCache()
    {
        var output = ProcessRunner.Run("nbtstat", "-c");
        if (string.IsNullOrWhiteSpace(output)) yield break;
        yield return new NetworkAuditItem
        {
            Kind = NetworkAuditKind.NetbiosCache,
            FilterGroup = NetworkAuditFilterGroup.Cache,
            Source = "nbtstat -c",
            Title = "NetBIOS кэш имён в сети",
            Detail = Truncate(output, 800),
            Location = "__netbios__",
            CanClean = true
        };
    }

    public static IEnumerable<NetworkAuditItem> ScanWlansvcFiles()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Wlansvc\Profiles\Interfaces");
        if (!Directory.Exists(dir)) yield break;
        foreach (var iface in Directory.EnumerateDirectories(dir))
        {
            foreach (var xml in Directory.EnumerateFiles(iface, "*.xml"))
            {
                var info = new FileInfo(xml);
                yield return new NetworkAuditItem
                {
                    Kind = NetworkAuditKind.WiFiProfile,
                    FilterGroup = NetworkAuditFilterGroup.WiFi,
                    Source = "Wlansvc\\Profiles",
                    Title = $"Wi‑Fi XML: {Path.GetFileNameWithoutExtension(xml)}",
                    Detail = $"Путь: {xml} | Изменён: {info.LastWriteTime:dd.MM.yyyy HH:mm}",
                    Location = xml,
                    CanClean = true
                };
            }
        }
    }

    private static string Match(string text, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
