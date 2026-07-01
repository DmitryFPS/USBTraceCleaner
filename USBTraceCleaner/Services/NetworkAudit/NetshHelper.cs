using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

internal static partial class NetshHelper
{
    public static IEnumerable<NetworkAuditItem> ScanWiFiProfiles()
    {
        var output = ProcessRunner.Run("netsh", "wlan show profiles");
        var names = ProfileNameRegex().Matches(output)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in names)
        {
            var detail = ProcessRunner.Run("netsh", $"wlan show profile name=\"{name}\"");
            var key = ProcessRunner.Run("netsh", $"wlan show profile name=\"{name}\" key=clear");

            var auth = MatchValue(detail, @"Authentication\s*:\s*(.+)");
            var cipher = MatchValue(detail, @"Cipher\s*:\s*(.+)");
            var last = MatchValue(detail, @"Last\s+connected\s+on\s*:\s*(.+)", ignoreCase: true);
            var password = MatchValue(key, @"Key Content\s*:\s*(.+)", ignoreCase: true);

            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.WiFiProfile,
                FilterGroup = NetworkAuditFilterGroup.WiFi,
                Source = "netsh wlan",
                Title = $"Профиль Wi‑Fi: {name}",
                Detail = $"Auth: {auth} | Cipher: {cipher} | Последнее: {last}",
                Secret = string.IsNullOrWhiteSpace(password) ? null : password,
                Location = name,
                CanClean = true
            };
        }
    }

    public static IEnumerable<NetworkAuditItem> ScanInterfaces()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                continue;

            var props = nic.GetIPProperties();
            var addrs = string.Join(", ", props.UnicastAddresses.Select(a => a.Address.ToString()));
            var gw = string.Join(", ", props.GatewayAddresses.Select(g => g.Address.ToString()));
            var dns = string.Join(", ", props.DnsAddresses.Select(d => d.ToString()));

            var group = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => NetworkAuditFilterGroup.WiFi,
                NetworkInterfaceType.Ethernet => NetworkAuditFilterGroup.Ethernet,
                NetworkInterfaceType.Ppp => NetworkAuditFilterGroup.Vpn,
                _ => NetworkAuditFilterGroup.Ethernet
            };

            yield return new NetworkAuditItem
            {
                Kind = NetworkAuditKind.EthernetAdapter,
                FilterGroup = group,
                Source = "NetworkInterface",
                Title = $"{nic.Name} ({nic.NetworkInterfaceType})",
                Detail = $"Статус: {nic.OperationalStatus} | MAC: {nic.GetPhysicalAddress()} | IP: {addrs} | GW: {gw} | DNS: {dns}",
                Location = nic.Id,
                CanClean = false
            };
        }
    }

    public static string? GetDefaultGateway()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var gw = nic.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address;
            if (gw is { AddressFamily: AddressFamily.InterNetwork })
                return gw.ToString();
        }
        return null;
    }

    private static string MatchValue(string text, string pattern, bool ignoreCase = false)
    {
        var m = Regex.Match(text, pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return m.Success ? m.Groups[1].Value.Trim() : "—";
    }

    [GeneratedRegex(@"All User Profile\s*:\s*(.+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileNameRegex();
}
