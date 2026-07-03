using System.Text.RegularExpressions;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

public static class NetworkAuditClassifier
{
    private static readonly Regex IpRegex = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d?\d)\b",
        RegexOptions.Compiled);

    public static void ApplyWhitelist(IEnumerable<NetworkAuditItem> items, NetworkAuditWhitelist whitelist)
    {
        foreach (var item in items)
            item.AuthorizationStatus = Classify(item, whitelist);
    }

    public static NetworkAuthorizationStatus Classify(NetworkAuditItem item, NetworkAuditWhitelist whitelist)
    {
        var haystack = $"{item.Title} {item.Detail} {item.Location} {item.Source}";

        foreach (var ip in whitelist.AllowedIps)
        {
            if (haystack.Contains(ip, StringComparison.Ordinal))
                return NetworkAuthorizationStatus.Allowed;
        }

        foreach (var match in IpRegex.Matches(haystack).Cast<Match>())
        {
            if (whitelist.AllowedIps.Any(a => a.Equals(match.Value, StringComparison.Ordinal)))
                return NetworkAuthorizationStatus.Allowed;
        }

        foreach (var ssid in whitelist.AllowedWiFi)
        {
            if (haystack.Contains(ssid, StringComparison.OrdinalIgnoreCase))
                return NetworkAuthorizationStatus.Allowed;
        }

        foreach (var vpn in whitelist.AllowedVpn)
        {
            if (haystack.Contains(vpn, StringComparison.OrdinalIgnoreCase))
                return NetworkAuthorizationStatus.Allowed;
        }

        return NetworkAuthorizationStatus.Unknown;
    }
}
