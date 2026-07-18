using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
public static class NetworkPostCleanActions
{
    public static void RunFullCleanExtras(NetworkAuditWhitelist? whitelist = null, Action<string>? log = null)
    {
        void L(string msg) => log?.Invoke(msg);
        whitelist ??= new NetworkAuditWhitelist();

        L("--- Дополнительная глубокая очистка ---");
        DisconnectVpnSessions(L);
        DeleteAllWiFiProfiles(whitelist, L);
        DeleteWlanXmlProfiles(whitelist, L);
        ClearNetworkListRegistry(whitelist, L);
        FlushArp(L);
    }

    /// <summary>
    /// Разрывает сессии Wi‑Fi/VPN без отключения физических адаптеров (код 22).
    /// </summary>
    public static void DisconnectNetwork(bool rebootScheduled, Action<string>? log = null)
    {
        void L(string msg) => log?.Invoke(msg);
        L("--- Отключение сетевых подключений ---");

        try
        {
            ProcessRunner.Run("netsh", "wlan disconnect");
            L("  ✓ Wi‑Fi сессия разорвана (адаптер остаётся включённым)");
        }
        catch (Exception ex)
        {
            L($"  — Wi‑Fi disconnect: {ex.Message}");
        }

        DisconnectVpnSessions(L);

        if (rebootScheduled)
        {
            L("  — Физические адаптеры не отключаются (перезагрузка через минуту)");
            return;
        }

        try
        {
            ProcessRunner.Run("powershell", "-NoProfile -Command \"" +
                "Get-NetAdapter | Where-Object { " +
                "$_.Status -eq 'Up' -and " +
                "$_.InterfaceDescription -notlike '*Hyper-V*' -and " +
                "$_.InterfaceDescription -notlike '*Loopback*' -and " +
                "$_.InterfaceDescription -notlike '*Virtual*' -and " +
                "($_.InterfaceDescription -like '*tun*' -or $_.InterfaceDescription -like '*Tunnel*' -or $_.InterfaceDescription -like '*WireGuard*' -or $_.InterfaceDescription -like '*OpenVPN*') " +
                "} | ForEach-Object { Disconnect-NetAdapter -Name $_.Name -Confirm:$false -ErrorAction SilentlyContinue }\"");
            L("  ✓ VPN-туннели отключены (физический Wi‑Fi/Ethernet не тронуты)");
        }
        catch (Exception ex)
        {
            L($"  — VPN-туннели: {ex.Message}");
        }
    }

    public static void ScheduleReboot(int seconds, Action<string>? log = null)
    {
        ProcessRunner.Run("shutdown", $"/r /t {seconds} /c \"USB Trace Cleaner — перезагрузка после очистки сети\"");
        log?.Invoke($"  ✓ Перезагрузка через {seconds} сек.");
    }

    public static void StopBlockingServices(Action<string>? log = null)
    {
        foreach (var name in new[] { "DPS", "DiagTrack" })
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    log?.Invoke($"  ✓ Служба остановлена: {name}");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"  — Служба {name}: {ex.Message}");
            }
        }
    }

    public static void StartBlockingServices(Action<string>? log = null)
    {
        foreach (var name in new[] { "DPS", "DiagTrack" })
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    log?.Invoke($"  ✓ Служба запущена: {name}");
                }
            }
            catch { /* ignore */ }
        }
    }

    public static bool TryDeleteSruDatabase(Action<string> log)
    {
        var sruDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sru");
        var sruFile = Path.Combine(sruDir, "SRUDB.dat");

        StopBlockingServices(log);
        Thread.Sleep(500);

        try
        {
            if (Directory.Exists(sruDir))
            {
                foreach (var file in Directory.EnumerateFiles(sruDir, "SRU*"))
                    TryDeleteOrTruncate(file, log);
            }

            if (File.Exists(sruFile) && new FileInfo(sruFile).Length > 64 * 1024)
            {
                log($"  ✗ SRUDB.dat: файл занят ({new FileInfo(sruFile).Length / 1024} KB)");
                return false;
            }

            log("  ✓ SRUDB.dat удалён или обнулён");
            return true;
        }
        catch (Exception ex)
        {
            log($"  ✗ SRUDB: {ex.Message}");
            return false;
        }
        finally
        {
            StartBlockingServices(log);
        }
    }

    internal static bool IsWhitelistedName(string? name, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return allowed.Any(a =>
            !string.IsNullOrWhiteSpace(a) &&
            name.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    private static void DisconnectVpnSessions(Action<string> log)
    {
        try
        {
            ProcessRunner.Run("rasdial", "/DISCONNECT");
            log("  ✓ VPN-сессии разорваны (rasdial)");
        }
        catch
        {
            log("  — rasdial: активных VPN-сессий нет");
        }
    }

    private static void DeleteAllWiFiProfiles(NetworkAuditWhitelist whitelist, Action<string> log)
    {
        try
        {
            var output = ProcessRunner.Run("netsh", "wlan show profiles");
            var names = output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains(':'))
                .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
                .Where(n => n.Length > 0 && !n.StartsWith('<'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in names)
            {
                if (IsWhitelistedName(name, whitelist.AllowedWiFi))
                {
                    log($"  — Wi‑Fi профиль сохранён (белый список): {name}");
                    continue;
                }

                ProcessRunner.Run("netsh", $"wlan delete profile name=\"{name}\"");
                log($"  ✓ Wi‑Fi профиль удалён: {name}");
            }
        }
        catch (Exception ex)
        {
            log($"  — Wi‑Fi профили: {ex.Message}");
        }
    }

    private static void DeleteWlanXmlProfiles(NetworkAuditWhitelist whitelist, Action<string> log)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Microsoft\Wlansvc\Profiles\Interfaces");
        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (var xml in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(xml);
                    if (whitelist.AllowedWiFi.Any(ssid =>
                            !string.IsNullOrWhiteSpace(ssid) &&
                            text.Contains(ssid, StringComparison.OrdinalIgnoreCase)))
                    {
                        log($"  — Wi‑Fi XML сохранён (белый список): {Path.GetFileName(xml)}");
                        continue;
                    }

                    File.Delete(xml);
                    log($"  ✓ Wi‑Fi XML удалён: {Path.GetFileName(xml)}");
                }
                catch (Exception ex)
                {
                    log($"  — XML {Path.GetFileName(xml)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log($"  — Wlansvc XML: {ex.Message}");
        }
    }

    private static void ClearNetworkListRegistry(NetworkAuditWhitelist whitelist, Action<string> log)
    {
        foreach (var path in new[]
        {
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Unmanaged",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Managed",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Nla\Cache"
        })
        {
            try
            {
                RegistryHelper.SafeOpen(key =>
                {
                    foreach (var sub in key.GetSubKeyNames().ToList())
                    {
                        try
                        {
                            using var child = key.OpenSubKey(sub);
                            var profileName = child?.GetValue("ProfileName") as string
                                              ?? child?.GetValue("Description") as string
                                              ?? string.Empty;
                            if (IsWhitelistedName(profileName, whitelist.AllowedWiFi) ||
                                IsWhitelistedName(profileName, whitelist.AllowedVpn) ||
                                IsWhitelistedName(sub, whitelist.AllowedIps))
                            {
                                log($"  — Реестр сохранён (белый список): {path}\\{sub}");
                                continue;
                            }

                            key.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                        }
                        catch { /* ignore */ }
                    }
                }, RegistryHive.LocalMachine, path, writable: true);
                log($"  ✓ Очищен реестр: {path}");
            }
            catch (Exception ex)
            {
                log($"  — {path}: {ex.Message}");
            }
        }
    }

    private static void FlushArp(Action<string> log)
    {
        try
        {
            ProcessRunner.Run("arp", "-d *");
            log("  ✓ ARP-кэш сброшен");
        }
        catch (Exception ex)
        {
            log($"  — ARP: {ex.Message}");
        }
    }

    private static void TryDeleteOrTruncate(string path, Action<string> log)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                return;
            }
            catch when (attempt < 2)
            {
                Thread.Sleep(300);
            }
            catch
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                    fs.SetLength(0);
                    log($"  — {Path.GetFileName(path)}: удаление не удалось, файл обнулён");
                    return;
                }
                catch (Exception ex)
                {
                    log($"  ✗ {Path.GetFileName(path)}: {ex.Message}");
                    throw;
                }
            }
        }
    }
}
