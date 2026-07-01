using System.IO;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

public sealed class NetworkAuditCleaner
{
    public sealed class CleanResult
    {
        public int Processed { get; init; }
        public int Failed { get; init; }
        public string Log { get; init; } = string.Empty;
    }

    public CleanResult Execute(IEnumerable<NetworkAuditItem> items, NetworkAuditOptions options, Action<string>? log = null)
    {
        var sb = new System.Text.StringBuilder();
        void L(string msg)
        {
            sb.AppendLine(msg);
            log?.Invoke(msg);
        }

        var selected = items.Where(i => i.Selected && i.CanClean).ToList();
        var processed = 0;
        var failed = 0;

        L($"Очистка сетевых следов: {selected.Count} элементов");
        if (options.SimulationMode)
        {
            L("Режим симуляции — удаление не выполнялось.");
            return new CleanResult { Processed = 0, Failed = 0, Log = sb.ToString() };
        }

        foreach (var item in selected)
        {
            try
            {
                if (CleanOne(item, L))
                    processed++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                failed++;
                L($"  ✗ {item.Title}: {ex.Message}");
            }
        }

        L($"Готово. Успешно: {processed}, ошибок: {failed}");
        return new CleanResult { Processed = processed, Failed = failed, Log = sb.ToString() };
    }

    private static bool CleanOne(NetworkAuditItem item, Action<string> log)
    {
        switch (item.Kind)
        {
            case NetworkAuditKind.WiFiProfile when item.Location.EndsWith(".xml", StringComparison.OrdinalIgnoreCase):
                if (File.Exists(item.Location))
                {
                    File.Delete(item.Location);
                    log($"  ✓ Удалён XML Wi‑Fi: {item.Location}");
                    return true;
                }
                return false;

            case NetworkAuditKind.WiFiProfile:
                ProcessRunner.Run("netsh", $"wlan delete profile name=\"{item.Location}\"");
                log($"  ✓ Удалён профиль Wi‑Fi: {item.Location}");
                return true;

            case NetworkAuditKind.DnsCache when item.Location == "__flushdns__":
                ProcessRunner.Run("ipconfig", "/flushdns");
                log("  ✓ DNS-кэш очищен");
                return true;

            case NetworkAuditKind.DnsCache:
                return true;

            case NetworkAuditKind.EventLogChannel:
                ProcessRunner.Run("wevtutil", $"cl \"{item.Location}\"");
                log($"  ✓ Очищен журнал: {item.Location}");
                return true;

            case NetworkAuditKind.NetworkProfile:
                RegistryHelper.SafeOpen(_ => { },
                    RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\{item.Location}",
                    writable: true);
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\{item.Location}");
                log($"  ✓ Удалён профиль сети: {item.Location}");
                return true;

            case NetworkAuditKind.NlaCache:
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Nla\Cache\{item.Location}");
                log($"  ✓ Удалён NLA cache: {item.Location}");
                return true;

            case NetworkAuditKind.VpnProfile when File.Exists(item.Location):
                File.Delete(item.Location);
                log($"  ✓ Удалён VPN файл: {item.Location}");
                return true;

            case NetworkAuditKind.VpnProfile:
                TryDeleteRegistryKey(RegistryHive.CurrentUser,
                    $@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections\{item.Location}");
                log($"  ✓ Удалён VPN-профиль: {item.Location}");
                return true;

            case NetworkAuditKind.NetbiosCache:
                ProcessRunner.Run("nbtstat", "-R");
                log("  ✓ NetBIOS кэш сброшен");
                return true;

            default:
                log($"  — Пропуск (не поддерживается): {item.Title}");
                return false;
        }
    }

    private static void TryDeleteRegistryKey(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch { /* ignore */ }
    }
}
