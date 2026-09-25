using System.Text.RegularExpressions;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

internal sealed record DeviceIdentity(string InstanceId, string Name, string? ContainerId = null,
    string? Driver = null, bool? Present = null, string? Service = null);

/// <summary>A scan-local inventory. Never guesses a physical device from a serial substring.</summary>
internal sealed class DeviceIdentityResolver(IEnumerable<DeviceIdentity> devices)
{
    private readonly DeviceIdentity[] _devices = devices.ToArray();
    private static readonly Regex VidPid = new(@"VID_([0-9A-F]{4})[&+]PID_([0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal void Enrich(ArtifactItem item, string? valueData = null)
    {
        var text = Normalize(string.Join("|", item.Location, item.ValueName, item.Detail, valueData));
        var matches = _devices.Where(d => ContainsToken(text, Normalize(d.InstanceId)) ||
            (!string.IsNullOrWhiteSpace(d.ContainerId) && ContainsToken(text, d.ContainerId)) ||
            (!string.IsNullOrWhiteSpace(d.Driver) && ContainsToken(text, @"Control\Class\" + d.Driver)))
            .ToArray();
        var source = "Имя из Windows · связь по записи устройства";

        if (matches.Length == 0 && item.Type == ArtifactType.RegistryKey)
        {
            var marker = item.Location.IndexOf(@"\Enum\", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var tail = item.Location[(marker + 6)..].TrimEnd('\\');
                matches = _devices.Where(d => d.InstanceId.StartsWith(tail + "\\", StringComparison.OrdinalIgnoreCase)).ToArray();
                source = "Общая ветвь реестра для перечисленных устройств";
            }
        }

        // VID/PID describes a model; it cannot identify a particular physical instance.
        if (matches.Length == 0 && !text.Contains(@"\ENUM\", StringComparison.OrdinalIgnoreCase))
        {
            var pair = VidPid.Match(text);
            var vid = pair.Success ? pair.Groups[1].Value : item.Vid;
            var pid = pair.Success ? pair.Groups[2].Value : item.Pid;
            if (vid != null && pid != null)
                matches = _devices.Where(d => d.InstanceId.Contains($"VID_{vid}&PID_{pid}", StringComparison.OrdinalIgnoreCase)).ToArray();
            source = "Совпадение модели VID/PID · конкретный экземпляр не определён";
        }

        if (matches.Length > 0)
        {
            var names = matches.Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            item.DeviceName = names.Length == 0 ? "Название не сохранено в Windows" : string.Join(" / ", names.Take(3));
            if (names.Length > 3) item.DeviceName += $" · ещё {names.Length - 3}";
            item.DeviceNameSource = source;
            item.RelatedDeviceIds = matches.Select(d => d.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            item.DevicePresent = matches.Any(d => d.Present == true) ? true : matches.All(d => d.Present == false) ? false : null;
            return;
        }

        var storageName = NameFromInstanceId(text);
        if (storageName != null)
        {
            item.DeviceName = storageName;
            item.DeviceNameSource = "Модель из идентификатора накопителя · состояние неизвестно";
        }
        else if (item.DisplayModel != "—")
        {
            item.DeviceName = item.DisplayModel;
            item.DeviceNameSource = "Справочник VID/PID · конкретный экземпляр не определён";
        }
        else if (item.Type is ArtifactType.EventLog or ArtifactType.File ||
            item.Category is ArtifactCategory.RegistryShell or ArtifactCategory.RegistryUser)
        {
            item.DeviceName = "Общие записи — несколько устройств";
            item.DeviceNameSource = "Нельзя достоверно связать с одним устройством";
        }
    }

    internal static string? ReadableName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.StartsWith('@'))
        {
            var separator = result.LastIndexOf(';');
            if (separator < 0) return null;
            result = result[(separator + 1)..].Trim();
        }
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    internal static string? NameFromInstanceId(string id)
    {
        var match = Regex.Match(id, @"(?:Disk|CdRom)&Ven_([^&\\|]*)&Prod_([^&\\|]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return (match.Groups[1].Value + " " + match.Groups[2].Value).Replace('_', ' ').Trim();
    }

    private static string Normalize(string value) => value.Replace('#', '\\');

    private static bool ContainsToken(string text, string token)
    {
        var start = 0;
        while ((start = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + token.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) &&
                (end == text.Length || text[end] is '\\' or '|' or '{' or '}' or '\0')) return true;
            start++;
        }
        return false;
    }
}
