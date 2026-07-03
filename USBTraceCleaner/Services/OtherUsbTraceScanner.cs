using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public sealed class OtherUsbTraceScanner
{
    private static readonly Regex VidPidRegex = new(
        @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // usbflags: имена ключей вида 0E0F00020000 (VID+PID+ревизия, 12 hex).
    private static readonly Regex UsbFlagsCompact = new(
        @"^([0-9A-Fa-f]{4})([0-9A-Fa-f]{4})[0-9A-Fa-f]{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // usbflags: IgnoreHWSerNum0E0F0003 и т.п. (VID+PID в конце, 8 hex).
    private static readonly Regex UsbFlagsSuffix = new(
        @"([0-9A-Fa-f]{4})([0-9A-Fa-f]{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Guid FirstInstallProperty =
        new("83da6326-97a6-4088-9453-a1923f573b29");

    private readonly Dictionary<string, MutableTrace> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<OtherUsbTraceItem> Scan(Action<string>? log = null)
    {
        _byKey.Clear();

        var controlSets = RegistryHelper.EnumerateControlSets().ToList();
        if (controlSets.Count == 0)
        {
            var current = RegistryHelper.GetCurrentControlSetName();
            if (!string.IsNullOrEmpty(current))
                controlSets.Add(current);
        }

        log?.Invoke($"ControlSet: {string.Join(", ", controlSets)}");

        foreach (var controlSet in controlSets)
        {
            var prefix = $@"SYSTEM\{controlSet}";
            ScanUsbFlags(prefix, log);
            ScanDeviceMigration($@"{prefix}\Control\DeviceMigration", OtherUsbTraceSource.DeviceMigration, log);
            ScanDeviceMigration($@"{prefix}\Control\DeviceMigration\Devices\USB", OtherUsbTraceSource.DeviceMigration, log);
        }

        ScanDeviceMigration(@"SYSTEM\Setup\Upgrade\PnP\CurrentControlSet\Control\DeviceMigration",
            OtherUsbTraceSource.SetupUpgradeMigration, log);
        ScanDeviceMigration(@"SYSTEM\Setup\Upgrade\PnP\CurrentControlSet\Control\DeviceMigration\Devices\USB",
            OtherUsbTraceSource.SetupUpgradeMigration, log);
        ScanDeviceMigration(@"SYSTEM\Setup\Upgrade\Pnp\CurrentControlSet\Control\DeviceMigration",
            OtherUsbTraceSource.SetupUpgradeMigration, log);
        ScanDeviceMigration(@"SYSTEM\Setup\Upgrade\Control\DeviceMigration",
            OtherUsbTraceSource.SetupUpgradeMigration, log);

        ScanSetupUpgradeResidual(log);

        log?.Invoke($"Итого устройств: {_byKey.Count}");
        return _byKey.Values
            .Select(m => m.ToItem())
            .OrderBy(i => i.Vid)
            .ThenBy(i => i.Pid)
            .ToList();
    }

    /// <summary>usbflags — кэш USB-дескрипторов, главный источник «остаточных следов».</summary>
    private void ScanUsbFlags(string systemPrefix, Action<string>? log)
    {
        var root = $@"{systemPrefix}\Control\usbflags";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
        {
            log?.Invoke($"  [нет] {root}");
            return;
        }

        var count = 0;
        foreach (var name in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!TryParseUsbFlags(name, out var vid, out var pid))
                continue;

            var path = $@"{root}\{name}";
            EnrichFromEnum(systemPrefix, vid, pid, out var manufacturer, out var model, out var first);

            AddTrace(vid, pid, manufacturer, model, first, OtherUsbTraceSource.UsbFlags, name, path, name);
            count++;
        }

        log?.Invoke($"  usbflags ({systemPrefix}): {count}");
    }

    private static bool TryParseUsbFlags(string name, out string vid, out string pid)
    {
        vid = pid = "";
        var m = UsbFlagsCompact.Match(name);
        if (m.Success)
        {
            vid = m.Groups[1].Value;
            pid = m.Groups[2].Value;
            return true;
        }

        if (name.StartsWith("Ignore", StringComparison.OrdinalIgnoreCase))
        {
            var m2 = UsbFlagsSuffix.Match(name);
            if (m2.Success)
            {
                vid = m2.Groups[1].Value;
                pid = m2.Groups[2].Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Подтягивает имя из Enum\USB, если устройство ещё там (для usbflags-записей).</summary>
    private static void EnrichFromEnum(string systemPrefix, string vid, string pid,
        out string manufacturer, out string model, out DateTime? first)
    {
        manufacturer = "—";
        model = "—";
        first = null;

        var vidPidPath = $@"{systemPrefix}\Enum\USB\VID_{vid}&PID_{pid}";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, vidPidPath))
            return;

        foreach (var instance in RegistryQueryHelper.EnumerateImmediateSubKeyPaths(RegistryHive.LocalMachine, vidPidPath))
        {
            var instancePath = $@"{vidPidPath}\{instance}";
            var mfg = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "Mfg") ?? "";
            var desc = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "DeviceDesc") ?? "";
            var friendly = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, instancePath, "FriendlyName") ?? "";
            ParseNames(mfg, desc, friendly, out manufacturer, out model);
            first = ReadFirstInstallDate(instancePath);
            return;
        }
    }

    /// <summary>DeviceMigration / Setup\Upgrade — второй источник «Других следов».</summary>
    private void ScanDeviceMigration(string root, OtherUsbTraceSource source, Action<string>? log)
    {
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
        {
            log?.Invoke($"  [нет] {root}");
            return;
        }

        var count = 0;
        foreach (var path in RegistryQueryHelper.EnumerateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!VidPidRegex.IsMatch(path) || StorageTracePatterns.MatchesStorage(path))
                continue;

            var match = VidPidRegex.Match(path);
            count++;
            var instanceId = Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar));
            var first = ReadLastPresentDate(path);
            var desc = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, path, "Description") ?? "";
            var friendly = RegistryHelper.GetStringValueAt(RegistryHive.LocalMachine, path, "FriendlyName") ?? "";
            ParseNames("", desc, friendly, out var manufacturer, out var model);

            AddTrace(match.Groups[1].Value, match.Groups[2].Value, manufacturer, model,
                first, source, instanceId, path, desc);
        }

        if (count > 0)
            log?.Invoke($"  {root}: {count}");
    }

    private void ScanSetupUpgradeResidual(Action<string>? log)
    {
        const string root = @"SYSTEM\Setup\Upgrade";
        if (!RegistryHelper.KeyExists(RegistryHive.LocalMachine, root))
            return;

        var count = 0;
        foreach (var path in RegistryQueryHelper.EnumerateSubKeyPaths(RegistryHive.LocalMachine, root))
        {
            if (!path.Contains("DeviceMigration", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!VidPidRegex.IsMatch(path) || StorageTracePatterns.MatchesStorage(path))
                continue;

            var match = VidPidRegex.Match(path);
            count++;
            AddTrace(match.Groups[1].Value, match.Groups[2].Value, "—", "—",
                ReadLastPresentDate(path), OtherUsbTraceSource.SetupUpgradeMigration,
                Path.GetFileName(path), path, path);
        }

        if (count > 0)
            log?.Invoke($"  Setup\\Upgrade (остатки): {count}");
    }

    private void AddTrace(
        string vid, string pid, string manufacturer, string model,
        DateTime? firstConnected, OtherUsbTraceSource source,
        string instanceId, string registryPath, string detail)
    {
        var key = $"{vid.ToUpperInvariant()}|{pid.ToUpperInvariant()}";
        if (!_byKey.TryGetValue(key, out var trace))
        {
            trace = new MutableTrace(vid, pid, instanceId);
            _byKey[key] = trace;
        }

        trace.Merge(manufacturer, model, firstConnected, source, detail, registryPath);
    }

    private static DateTime? ReadLastPresentDate(string keyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(keyPath);
            var raw = key?.GetValue("LastPresentDate") as byte[];
            if (raw == null || raw.Length < 8) return null;
            var fileTime = BitConverter.ToInt64(raw, 0);
            if (fileTime <= 0) return null;
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch { return null; }
    }

    private static DateTime? ReadFirstInstallDate(string enumInstancePath)
    {
        var propertyKey = $@"{enumInstancePath}\Properties\{FirstInstallProperty:B}";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(propertyKey);
            var raw = key?.GetValue("0064") as byte[];
            if (raw == null || raw.Length < 8) return null;
            var fileTime = BitConverter.ToInt64(raw, 0);
            if (fileTime <= 0) return null;
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch { return null; }
    }

    private static void ParseNames(string mfg, string deviceDesc, string friendlyName,
        out string manufacturer, out string model)
    {
        manufacturer = CleanRegistryString(mfg);
        model = CleanRegistryString(friendlyName);
        var desc = CleanRegistryString(deviceDesc);
        if (string.IsNullOrWhiteSpace(model)) model = desc;

        if (string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(desc))
        {
            var comma = desc.IndexOf(',');
            if (comma > 0)
            {
                manufacturer = desc[..comma].Trim().TrimStart('@');
                if (string.IsNullOrWhiteSpace(model))
                    model = desc[(comma + 1)..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(manufacturer)) manufacturer = "—";
        if (string.IsNullOrWhiteSpace(model)) model = "—";
    }

    private static string CleanRegistryString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Trim();
        if (value.StartsWith('@')) value = value[1..];
        var semi = value.IndexOf(';');
        if (semi > 0) value = value[..semi];
        return value.Trim().Trim('"');
    }

    private sealed class MutableTrace
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<OtherUsbTraceSource> _sources = [];

        public MutableTrace(string vid, string pid, string instanceId)
        {
            Vid = vid;
            Pid = pid;
            InstanceId = instanceId;
        }

        public string Vid { get; }
        public string Pid { get; }
        public string InstanceId { get; }
        public string Manufacturer { get; private set; } = "—";
        public string Model { get; private set; } = "—";
        public DateTime? FirstConnected { get; private set; }
        public string Detail { get; private set; } = "";

        public void Merge(string manufacturer, string model, DateTime? firstConnected,
            OtherUsbTraceSource source, string detail, string registryPath)
        {
            _sources.Add(source);
            _paths.Add(registryPath);

            if (Manufacturer == "—" && manufacturer != "—") Manufacturer = manufacturer;
            if (Model == "—" && model != "—") Model = model;
            if (firstConnected.HasValue && (!FirstConnected.HasValue || firstConnected < FirstConnected))
                FirstConnected = firstConnected;
            if (string.IsNullOrWhiteSpace(Detail) && !string.IsNullOrWhiteSpace(detail))
                Detail = detail;
        }

        public OtherUsbTraceItem ToItem()
        {
            var paths = OtherUsbPathCollector.CollectRegistryPaths(Vid, Pid);
            var logs = OtherUsbPathCollector.CollectSetupApiLogs(Vid, Pid);
            foreach (var p in _paths) paths.Add(p);

            var source = _sources.OrderBy(s => s).First();
            var manufacturer = Manufacturer;
            var model = Model;
            if (manufacturer == "—")
                manufacturer = UsbVendorDatabase.LookupVendor(Vid) ?? "—";
            if (model == "—")
                model = UsbVendorDatabase.LookupProduct(Vid, Pid) ?? "—";

            return new OtherUsbTraceItem
            {
                Vid = Vid,
                Pid = Pid,
                Manufacturer = manufacturer,
                Model = model,
                FirstConnected = FirstConnected,
                SourceKind = source,
                SourceLabel = FormatSource(_sources, logs.Count > 0),
                InstanceId = InstanceId,
                RegistryPaths = paths.OrderByDescending(p => p.Length).ToList(),
                LogFilePaths = logs,
                Detail = Detail
            };
        }

        private static string FormatSource(IEnumerable<OtherUsbTraceSource> all, bool hasLogs)
        {
            static string Label(OtherUsbTraceSource s) => s switch
            {
                OtherUsbTraceSource.UsbFlags => "usbflags",
                OtherUsbTraceSource.EnumUsb => "Enum\\USB",
                OtherUsbTraceSource.DeviceMigration => "DeviceMigration",
                OtherUsbTraceSource.SetupUpgradeMigration => "Setup\\Upgrade",
                _ => "Прочее"
            };

            var labels = all.Select(Label).Distinct().OrderBy(x => x).ToList();
            if (hasLogs) labels.Add("setupapi");
            return labels.Count == 0 ? "Прочее" : string.Join(", ", labels);
        }
    }
}
