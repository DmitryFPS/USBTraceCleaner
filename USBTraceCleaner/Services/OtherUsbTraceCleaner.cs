using System.Text;
using Microsoft.Win32;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services;

public static class OtherUsbTraceCleaner
{
    public static OtherUsbTraceCleanResult Execute(IEnumerable<OtherUsbTraceItem> items,
        bool simulation, Action<string>? log = null)
    {
        var sb = new StringBuilder();
        void L(string line) { sb.AppendLine(line); log?.Invoke(line); }
        var selected = items.Where(i => i.Selected).ToArray();
        if (!simulation && !AdminHelper.IsAdministrator())
            return new() { ErrorMessage = "Требуются права администратора." };
        if (selected.Length == 0)
            return new() { Success = true, Log = "Нет выбранных записей." };

        L(simulation ? "СИМУЛЯЦИЯ — изменения не выполняются" : "Удаление выбранных записей реестра");
        var failed = new List<string>();
        var processed = 0;
        // Use the reviewed scan snapshot, never expand selection to every device of a model.
        foreach (var path in selected.SelectMany(i => i.RegistryPaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (simulation) { L($"[SIM] KEY {path}"); processed++; continue; }
            try
            {
                var ids = selected.Where(i => i.RegistryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    .SelectMany(i => i.RelatedDeviceIds);
                if (ids.Any(DeviceUninstallHelper.IsDevicePresent) || DeviceRegistryGuard.ContainsPresentDevice(path))
                {
                    failed.Add(path);
                    L($"[FAIL] Запись связана с подключённым устройством: {path}");
                    continue;
                }
                if (RegistryHelper.DeleteKey(RegistryHive.LocalMachine, path, false, L)) processed++;
                else failed.Add(path);
            }
            catch (Exception ex) { failed.Add(path); L($"[FAIL] {path}: {ex.Message}"); }
        }
        if (selected.Any(i => i.LogFilePaths.Count > 0))
            L("Общие файлы журналов не изменены: они относятся к нескольким устройствам.");
        return new()
        {
            Success = failed.Count == 0, Processed = processed, Failed = failed.Count,
            FailedPaths = failed, Log = sb.ToString(),
            Hint = failed.Count > 0 ? "Отключите устройство и повторите сканирование. Подробности — в журнале." : null
        };
    }
}
