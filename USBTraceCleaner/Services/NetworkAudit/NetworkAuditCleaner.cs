using System.IO;
using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
public sealed class NetworkAuditCleaner
{
    public sealed class CleanResult
    {
        public int Processed { get; init; }
        public int Failed { get; init; }
        public int Skipped { get; init; }
        public string Log { get; init; } = string.Empty;
        public IReadOnlyList<string> Failures { get; init; } = [];
        public IReadOnlyList<string> SkippedItems { get; init; } = [];
    }

    public CleanResult Execute(IEnumerable<NetworkAuditItem> items, NetworkAuditOptions options, Action<string>? log = null)
    {
        var sb = new StringBuilder();
        var failures = new List<string>();
        var skipped = new List<string>();

        void L(string msg)
        {
            sb.AppendLine(msg);
            log?.Invoke(msg);
        }

        void Fail(string title, string reason)
        {
            failures.Add($"{title}: {reason}");
            L($"  ✗ {title}: {reason}");
        }

        void Skip(string title, string reason)
        {
            skipped.Add($"{title}: {reason}");
            L($"  — {title}: {reason}");
        }

        var selected = items.Where(i => i.Selected && i.CanClean).ToList();
        if (options.FullCleanMode)
        {
            selected = items.Where(i => i.CanClean).ToList();
            foreach (var item in selected)
                item.Selected = true;
        }

        var processed = 0;
        var failed = 0;
        var skippedCount = 0;

        L($"Очистка сетевых следов: {selected.Count} элементов");
        if (options.SimulationMode)
        {
            L("Режим симуляции — удаление не выполнялось.");
            return new CleanResult { Log = sb.ToString() };
        }

        if (options.FullCleanMode)
        {
            NetworkPostCleanActions.StopBlockingServices(L);
            NetworkPostCleanActions.RunFullCleanExtras(options.Whitelist, L);
        }

        foreach (var item in selected)
        {
            if (item.AuthorizationStatus == NetworkAuthorizationStatus.Allowed)
            {
                skippedCount++;
                Skip(item.Title, "белый список — сохранено");
                continue;
            }

            if (item.Kind == NetworkAuditKind.HostsFile && !options.CleanHostsFile)
            {
                L($"  — Пропуск hosts (не подтверждено): {item.Title}");
                continue;
            }

            try
            {
                var outcome = CleanOne(item, L);
                switch (outcome.Kind)
                {
                    case CleanOutcomeKind.Success:
                        processed++;
                        break;
                    case CleanOutcomeKind.Skipped:
                        skippedCount++;
                        Skip(item.Title, outcome.Reason ?? "пропущено");
                        break;
                    case CleanOutcomeKind.Failed:
                        failed++;
                        Fail(item.Title, outcome.Reason ?? "не удалось");
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                Fail(item.Title, ex.Message);
            }
        }

        if (options.FullCleanMode)
            NetworkPostCleanActions.StartBlockingServices(L);

        if (options.DisconnectNetwork)
            NetworkPostCleanActions.DisconnectNetwork(options.RebootAfterClean, L);

        L($"Готово. Успешно: {processed}, пропущено: {skippedCount}, ошибок: {failed}");

        if (options.FullCleanMode)
        {
            var verify = NetworkAuditVerifier.Verify();
            L("--- Самопроверка ---");
            L(verify.Summary);
            foreach (var issue in verify.RemainingIssues)
                L($"  ⚠ {issue}");
        }

        if (options.RebootAfterClean)
        {
            NetworkPostCleanActions.ScheduleReboot(60, L);
            L("  ℹ После перезагрузки можно сохранить «Отчёт PDF» без Wi‑Fi, затем включить сеть.");
        }

        return new CleanResult
        {
            Processed = processed,
            Failed = failed,
            Skipped = skippedCount,
            Failures = failures,
            SkippedItems = skipped,
            Log = sb.ToString()
        };
    }

    private enum CleanOutcomeKind { Success, Skipped, Failed }

    private readonly struct CleanOutcome
    {
        public CleanOutcomeKind Kind { get; }
        public string? Reason { get; }
        private CleanOutcome(CleanOutcomeKind kind, string? reason = null)
        {
            Kind = kind;
            Reason = reason;
        }
        public static CleanOutcome Success => new(CleanOutcomeKind.Success);
        public static CleanOutcome Skipped(string reason) => new(CleanOutcomeKind.Skipped, reason);
        public static CleanOutcome Failed(string reason) => new(CleanOutcomeKind.Failed, reason);
    }

    private static CleanOutcome CleanOne(NetworkAuditItem item, Action<string> log)
    {
        switch (item.Kind)
        {
            case NetworkAuditKind.WiFiProfile when item.Location.EndsWith(".xml", StringComparison.OrdinalIgnoreCase):
                if (!File.Exists(item.Location))
                {
                    log($"  ✓ Wi‑Fi XML уже удалён: {Path.GetFileName(item.Location)}");
                    return CleanOutcome.Success;
                }
                File.Delete(item.Location);
                log($"  ✓ Удалён XML Wi‑Fi: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.WiFiProfile:
                ProcessRunner.Run("netsh", $"wlan delete profile name=\"{item.Location}\"");
                log($"  ✓ Удалён профиль Wi‑Fi: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.DnsCache when item.Location == "__flushdns__":
                ProcessRunner.Run("ipconfig", "/flushdns");
                log("  ✓ DNS-кэш очищен");
                return CleanOutcome.Success;

            case NetworkAuditKind.DnsCache:
                return CleanOutcome.Success;

            case NetworkAuditKind.EventLogChannel:
                switch (ProcessRunner.TryClearEventLog(item.Location, out var evtErr))
                {
                    case ProcessRunner.EventLogClearResult.Success:
                        log($"  ✓ Очищен журнал: {item.Location}");
                        return CleanOutcome.Success;
                    case ProcessRunner.EventLogClearResult.SkippedNotFound:
                        return CleanOutcome.Skipped("канал отсутствует в этой версии Windows");
                    default:
                        return CleanOutcome.Failed(evtErr ?? "не удалось очистить журнал");
                }

            case NetworkAuditKind.NetworkProfile:
                if (!TryDeleteRegistryKey(RegistryHive.LocalMachine,
                        $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\{item.Location}"))
                    return CleanOutcome.Failed("ключ реестра занят или недоступен");
                log($"  ✓ Удалён профиль сети: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.RegistryTrace:
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Unmanaged\{item.Location}");
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Managed\{item.Location}");
                log($"  ✓ Удалена подпись сети/VPN: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.NlaCache:
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Nla\Cache\{item.Location}");
                log($"  ✓ Удалён NLA cache: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.VpnProfile when File.Exists(item.Location):
                File.Delete(item.Location);
                log($"  ✓ Удалён VPN файл: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.VpnProfile:
                TryDeleteRegistryKey(RegistryHive.CurrentUser,
                    $@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections\{item.Location}");
                log($"  ✓ Удалён VPN-профиль: {item.Location}");
                return CleanOutcome.Success;

            case NetworkAuditKind.NetbiosCache:
                ProcessRunner.Run("nbtstat", "-R");
                log("  ✓ NetBIOS кэш сброшен");
                return CleanOutcome.Success;

            case NetworkAuditKind.SruDatabase:
                return NetworkPostCleanActions.TryDeleteSruDatabase(log)
                    ? CleanOutcome.Success
                    : CleanOutcome.Failed("SRUDB.dat занят службой DPS — перезагрузите ПК и повторите очистку");

            case NetworkAuditKind.HostsFile:
                var (ok, err) = HostsFileHelper.ResetToDefault(item.Location, log);
                return ok ? CleanOutcome.Success : CleanOutcome.Failed(err ?? "hosts");

            case NetworkAuditKind.WlanRegistry:
                TryDeleteRegistryKey(RegistryHive.LocalMachine,
                    $@"SYSTEM\CurrentControlSet\Services\WlanSvc\Interfaces\{item.Location}");
                log($"  ✓ Удалён WLAN интерфейс в реестре: {item.Location}");
                return CleanOutcome.Success;

            default:
                return CleanOutcome.Failed("тип следа не поддерживается для удаления");
        }
    }

    private static bool TryDeleteRegistryKey(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            using var check = baseKey.OpenSubKey(subKey);
            return check == null;
        }
        catch
        {
            return false;
        }
    }
}
