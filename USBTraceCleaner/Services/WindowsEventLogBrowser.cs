using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace USBTraceCleaner.Services;

/// <summary>
/// Чтение/очистка журналов Event Viewer. Список каналов берётся с текущей машины
/// (как в eventvwr.msc) — на разных ПК набор разный. Не меняет USB ArtifactCleaner.
/// </summary>
[ExcludeFromCodeCoverage]
public static class WindowsEventLogBrowser
{
    public const int DefaultEventLimit = 400;
    public const string GroupAll = "Все";
    public const string GroupWindowsLogs = "Журналы Windows";
    public const string GroupOther = "Прочие";
    public const string GroupMicrosoft = "Microsoft";

    /// <summary>Классические «Журналы Windows» как в Просмотр событий.</summary>
    public static readonly HashSet<string> WindowsLogChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application",
        "Security",
        "Setup",
        "System",
        "ForwardedEvents",
    };

    private static readonly Dictionary<string, string> WindowsLogLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Application"] = "Приложение",
        ["Security"] = "Безопасность",
        ["Setup"] = "Установка",
        ["System"] = "Система",
        ["ForwardedEvents"] = "Пересылаемые события",
        ["HardwareEvents"] = "События оборудования",
    };

    private static readonly string[] UsbFilterTokens =
    [
        "USBSTOR", "USB\\VID", "USB#VID", "WPDBUSENUM", "UASPStor", "RemovableMedia",
        "UserPnp", "DeviceInstall", "WPD", "MTP", "UDisk", "Mass Storage",
    ];

    /// <summary>
    /// Все доступные каналы на этом ПК (динамически). Группы — как дерево Event Viewer.
    /// </summary>
    public static List<EventLogChannelRow> ListChannels()
    {
        IEnumerable<string> names;
        try
        {
            using var session = new EventLogSession();
            names = session.GetLogNames().ToList();
        }
        catch
        {
            return [];
        }

        var bag = new ConcurrentBag<EventLogChannelRow>();
        Parallel.ForEach(names, name =>
        {
            var row = TryGetChannelRow(name);
            if (row != null)
                bag.Add(row);
        });

        return bag
            .OrderBy(r => r.Group == GroupWindowsLogs ? 0 : 1)
            .ThenBy(r => r.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => WindowsLogChannels.Contains(r.Channel) ? WindowsLogOrder(r.Channel) : 100)
            .ThenBy(r => r.Channel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ClassifyGroup(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return "Прочее";

        if (WindowsLogChannels.Contains(channel))
            return GroupWindowsLogs;

        var head = channel;
        var slash = channel.IndexOf('/');
        if (slash >= 0)
            head = channel[..slash];

        if (head.StartsWith("Microsoft-", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
            || head.Equals(GroupMicrosoft, StringComparison.OrdinalIgnoreCase))
            return GroupMicrosoft;

        // Intel-xxx, Kaspersky-xxx → вендор; «Windows PowerShell» / «Internet Explorer» — целиком
        if (!head.Contains(' '))
        {
            var dash = head.IndexOf('-');
            if (dash > 0)
                return head[..dash];
        }

        return head;
    }

    public static string FriendlyLabel(string channel)
    {
        if (WindowsLogLabels.TryGetValue(channel, out var label))
            return label;

        var slash = channel.LastIndexOf('/');
        if (slash >= 0 && slash < channel.Length - 1)
            return channel[(slash + 1)..];

        return channel;
    }

    public static EventLogChannelRow? TryGetChannelRow(string channel, string? label = null, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;
        label ??= FriendlyLabel(channel);
        group ??= ClassifyGroup(channel);

        try
        {
            using var session = new EventLogSession();
            var info = session.GetLogInformation(channel, PathType.LogName);
            var enabled = true;
            try
            {
                using var cfg = new EventLogConfiguration(channel);
                enabled = cfg.IsEnabled;
            }
            catch
            {
                // analytic/debug и часть каналов не отдают конфигурацию
            }

            var isSecurity = channel.Equals("Security", StringComparison.OrdinalIgnoreCase);
            return new EventLogChannelRow
            {
                Channel = channel,
                Label = label,
                Group = group,
                Exists = true,
                IsEnabled = enabled,
                RecordCount = info.RecordCount,
                FileSizeBytes = info.FileSize,
                LastWrite = info.LastWriteTime,
                Selected = false,
                IsSecurity = isSecurity,
                IsSystem = channel.Equals("System", StringComparison.OrdinalIgnoreCase),
            };
        }
        catch
        {
            return null;
        }
    }

    public static List<EventLogEntryRow> ReadEvents(string channel, int limit, bool usbOnly)
    {
        var result = new List<EventLogEntryRow>();
        if (string.IsNullOrWhiteSpace(channel) || limit <= 0)
            return result;

        try
        {
            var query = new EventLogQuery(channel, PathType.LogName) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) != null)
            {
                using (record)
                {
                    var message = SafeFormat(record);
                    if (usbOnly && !LooksUsbRelated(message, record))
                        continue;

                    result.Add(new EventLogEntryRow
                    {
                        Time = record.TimeCreated,
                        Level = record.LevelDisplayName ?? LevelName(record.Level),
                        Id = record.Id,
                        Provider = record.ProviderName ?? "",
                        Message = Truncate(message, 800),
                    });

                    if (result.Count >= limit)
                        break;
                }
            }
        }
        catch
        {
            // канал недоступен для чтения
        }

        return result;
    }

    /// <summary>Таймаут одного вызова wevtutil (иначе UI «висит» на проблемном канале).</summary>
    public const int WevtutilTimeoutMs = 12_000;

    public static EventLogClearOutcome ClearChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return EventLogClearOutcome.Failed("пустой канал");

        var first = RunWevtutil($"cl \"{channel}\"");
        if (first.ExitCode == 0)
            return EventLogClearOutcome.Success;

        if (first.TimedOut)
            return EventLogClearOutcome.Failed("таймаут wevtutil");

        if (IsChannelNotFound(first.Output))
            return EventLogClearOutcome.Skip("канал отсутствует");

        if (IsAccessDenied(first.Output))
            return ClassifyClearFailure(first.Output);

        // Analytic/Debug нельзя очистить, пока включены — только для реально analytic/debug
        if (TryDisableAnalyticOrDebug(channel, out var wasEnabled))
        {
            var retry = RunWevtutil($"cl \"{channel}\"");
            if (wasEnabled)
                RunWevtutil($"sl \"{channel}\" /e:true");

            if (retry.ExitCode == 0)
                return EventLogClearOutcome.Success;

            if (retry.TimedOut)
                return EventLogClearOutcome.Failed("таймаут wevtutil (analytic/debug)");

            return ClassifyClearFailure(retry.Output);
        }

        return ClassifyClearFailure(first.Output);
    }

    /// <summary>
    /// Обычный wevtutil cl System всегда оставляет Event ID 104 «Система очищена» —
    /// это ограничение ОС. Чтобы убрать и его: cl → кратко остановить EventLog →
    /// удалить System.evtx → запустить службу (Windows создаст пустой журнал).
    /// </summary>
    public static EventLogClearOutcome PurgeSystemLogCompletely(Action<string>? log = null)
    {
        log?.Invoke("wevtutil cl System…");
        var clear = ClearChannel("System");
        if (!clear.Ok && !clear.WasSkipped)
            log?.Invoke($"[WARN] wevtutil System: {clear.Error}");

        var evtx = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "winevt", "Logs", "System.evtx");

        if (!File.Exists(evtx))
        {
            log?.Invoke("[OK] System.evtx отсутствует — нечего обнулять");
            return EventLogClearOutcome.Success;
        }

        var stopped = TryStopService("EventLog", log);
        try
        {
            ProcessExec.Run("takeown.exe", $"/F \"{evtx}\" /A", 15_000);
            ProcessExec.Run("icacls.exe", $"\"{evtx}\" /grant Administrators:F", 15_000);

            try
            {
                File.Delete(evtx);
                log?.Invoke("[OK] System.evtx удалён (остаточный Event ID 104 убран)");
                return EventLogClearOutcome.Success;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[WARN] Удаление System.evtx: {ex.Message}");
            }

            // Файл занят — пометить на удаление после перезагрузки
            if (NativeMethods.MoveFileEx(evtx, null, NativeMethods.MoveFileDelayUntilReboot))
            {
                log?.Invoke("[OK] System.evtx будет удалён при перезагрузке (остаточный 104 исчезнет после reboot)");
                return EventLogClearOutcome.Success;
            }

            return EventLogClearOutcome.Failed(
                "не удалось обнулить System.evtx — перезагрузите ПК и повторите, либо очистите вручную");
        }
        finally
        {
            if (stopped)
                TryStartService("EventLog", log);
        }
    }

    private static bool TryStopService(string name, Action<string>? log)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
                return false;

            log?.Invoke($"Остановка службы {name}…");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45));
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[WARN] Не удалось остановить {name}: {ex.Message}");
            return false;
        }
    }

    private static void TryStartService(string name, Action<string>? log)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                return;
            log?.Invoke($"Запуск службы {name}…");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45));
        }
        catch (Exception ex)
        {
            log?.Invoke($"[WARN] Не удалось запустить {name}: {ex.Message}");
        }
    }

    private static class NativeMethods
    {
        public const int MoveFileDelayUntilReboot = 0x4;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
    }

    private static EventLogClearOutcome ClassifyClearFailure(string output)
    {
        var text = output.Trim();
        if (string.IsNullOrEmpty(text))
            return EventLogClearOutcome.Failed("wevtutil вернул ошибку без текста");

        if (IsAccessDenied(text))
            return EventLogClearOutcome.AccessDenied;

        if (IsAnalyticStillEnabled(text))
            return EventLogClearOutcome.Failed("analytic/debug-журнал нельзя очистить, пока он включён");

        return EventLogClearOutcome.Failed(Truncate(text, 220));
    }

    private static bool TryDisableAnalyticOrDebug(string channel, out bool wasEnabled)
    {
        wasEnabled = false;
        try
        {
            using var cfg = new EventLogConfiguration(channel);
            if (cfg.LogType is not (EventLogType.Analytical or EventLogType.Debug))
                return false;

            wasEnabled = cfg.IsEnabled;
            if (!wasEnabled)
                return true;

            var disable = RunWevtutil($"sl \"{channel}\" /e:false");
            return disable.ExitCode == 0 && !disable.TimedOut;
        }
        catch
        {
            // Тип неизвестен — не трогаем wevtutil sl (иначе зависание/лишние вызовы на 1000+ каналах)
            return false;
        }
    }

    private static WevtutilResult RunWevtutil(string arguments)
    {
        var r = ProcessExec.Run("wevtutil.exe", arguments, WevtutilTimeoutMs);
        return new WevtutilResult(r.ExitCode, r.Combined, r.TimedOut);
    }

    private readonly record struct WevtutilResult(int ExitCode, string Output, bool TimedOut);

    public static bool LooksUsbRelated(string? message, EventRecord? record = null)
    {
        if (!string.IsNullOrEmpty(message))
        {
            foreach (var token in UsbFilterTokens)
            {
                if (message.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        var provider = record?.ProviderName ?? "";
        if (provider.Contains("UserPnp", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Kernel-PnP", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("WPD", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("DeviceSetup", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Storage-ClassPnP", StringComparison.OrdinalIgnoreCase)
            || provider.Contains("Partition", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static int WindowsLogOrder(string channel) => channel.ToUpperInvariant() switch
    {
        "APPLICATION" => 0,
        "SECURITY" => 1,
        "SETUP" => 2,
        "SYSTEM" => 3,
        "FORWARDEDEVENTS" => 4,
        _ => 50,
    };

    private static string SafeFormat(EventRecord record)
    {
        try
        {
            return record.FormatDescription() ?? record.Id.ToString();
        }
        catch
        {
            return record.Id.ToString();
        }
    }

    private static string LevelName(byte? level) => level switch
    {
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Information",
        5 => "Verbose",
        _ => "Unknown",
    };

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text[..max] + "…";
    }

    private static bool IsChannelNotFound(string message) =>
        message.Contains("channel could not be found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
        || message.Contains("не удалось найти указанный канал", StringComparison.OrdinalIgnoreCase)
        || message.Contains("16000", StringComparison.Ordinal);

    private static bool IsAccessDenied(string message) =>
        message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("отказано в доступе", StringComparison.OrdinalIgnoreCase)
        || message.Contains("0x5", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnalyticStillEnabled(string message) =>
        message.Contains("enabled", StringComparison.OrdinalIgnoreCase)
        || message.Contains("analytic", StringComparison.OrdinalIgnoreCase)
        || message.Contains("debug", StringComparison.OrdinalIgnoreCase);
}

public sealed class EventLogChannelRow : INotifyPropertyChanged
{
    private bool _selected;

    public string Channel { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public bool Exists { get; init; }
    public bool IsEnabled { get; init; }
    public long? RecordCount { get; init; }
    public long? FileSizeBytes { get; init; }
    public DateTime? LastWrite { get; init; }
    public bool IsSecurity { get; init; }
    public bool IsSystem { get; init; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public string DisplayName => string.IsNullOrEmpty(Label) ? Channel : Label;

    /// <summary>«Журналы Windows» всегда сверху, остальные — по алфавиту.</summary>
    public int GroupSortKey =>
        Group.Equals(WindowsEventLogBrowser.GroupWindowsLogs, StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>
    /// Подгруппа «канал» как в Event Viewer: часть до '/' (Microsoft-Windows-Kernel-PnP/…).
    /// Без слэша — отображаемое имя (Система, Приложение).
    /// </summary>
    public string ChannelFamily
    {
        get
        {
            var slash = Channel.IndexOf('/');
            if (slash > 0)
                return Channel[..slash];
            return DisplayName;
        }
    }

    public string DisplayCount => RecordCount?.ToString("N0") ?? "—";
    public string DisplaySize =>
        FileSizeBytes is null or < 0 ? "—" : FormatSize(FileSizeBytes.Value);
    public string DisplayLastWrite =>
        LastWrite?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
        return $"{bytes / (1024.0 * 1024):0.#} МБ";
    }
}

public sealed class EventLogEntryRow
{
    public DateTime? Time { get; init; }
    public string Level { get; init; } = "";
    public int Id { get; init; }
    public string Provider { get; init; } = "";
    public string Message { get; init; } = "";
    public string DisplayTime => Time?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "—";
}

public readonly struct EventLogClearOutcome
{
    public bool Ok { get; }
    public bool WasSkipped { get; }
    public bool WasAccessDenied { get; }
    public string? Error { get; }

    private EventLogClearOutcome(bool ok, bool skipped, bool accessDenied, string? error)
    {
        Ok = ok;
        WasSkipped = skipped;
        WasAccessDenied = accessDenied;
        Error = error;
    }

    public static EventLogClearOutcome Success => new(true, false, false, null);
    public static EventLogClearOutcome Skip(string reason) => new(false, true, false, reason);
    public static EventLogClearOutcome AccessDenied => new(false, false, true,
        "закрыт политикой Windows");
    public static EventLogClearOutcome Failed(string error) => new(false, false, false, error);
}
