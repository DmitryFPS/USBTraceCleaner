using System.IO;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
public static class NetworkAuditVerifier
{
    public static NetworkVerifyResult Verify()
    {
        var issues = new List<string>();

        var sru = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"sru\SRUDB.dat");
        if (File.Exists(sru) && new FileInfo(sru).Length > 64 * 1024)
            issues.Add($"SRUDB.dat всё ещё большой ({new FileInfo(sru).Length / 1024} KB)");

        var wlan = ProcessRunner.Run("netsh", "wlan show profiles");
        var names = wlan.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains(':'))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .Where(n => n.Length > 0 && !n.StartsWith('<'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count > 0)
            issues.Add($"Wi‑Fi профили пользователя: {names.Count}");

        var profileCount = CountRegistrySubkeys(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
        if (profileCount > 0)
            issues.Add($"Профили NetworkList в реестре: {profileCount}");

        var sigCount = CountRegistrySubkeys(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Unmanaged");
        if (sigCount > 0)
            issues.Add($"Подписи VPN/сетей в реестре: {sigCount}");

        try
        {
            var nla = ProcessRunner.Run("wevtutil", "gli Microsoft-Windows-NlaSvc/Operational");
            if (nla.Contains("numberOfLogRecords:") && !nla.Contains("numberOfLogRecords: 0"))
                issues.Add("Журнал NlaSvc не пуст");
        }
        catch { /* ignore */ }

        var summary = issues.Count == 0
            ? "Проверка пройдена: типичные следы сети на ПК не обнаружены."
            : $"Осталось замечаний: {issues.Count}";

        return new NetworkVerifyResult
        {
            Success = issues.Count == 0,
            RemainingIssues = issues,
            Summary = summary
        };
    }

    private static int CountRegistrySubkeys(string path)
    {
        var count = 0;
        RegistryHelper.SafeOpen(key => count = key.GetSubKeyNames().Length,
            RegistryHive.LocalMachine, path);
        return count;
    }
}
