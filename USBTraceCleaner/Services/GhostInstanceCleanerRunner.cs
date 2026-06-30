using System.Text;

namespace USBTraceCleaner.Services;

public static class GhostInstanceCleanerRunner
{
    public static int Run()
    {
        var log = new StringBuilder();
        void Write(string line)
        {
            log.AppendLine(line);
            Console.WriteLine(line);
        }

        Write("=== USB Trace Cleaner — призраки и дубликаты PnP ===");
        Write("");

        if (!AdminHelper.IsAdministrator())
        {
            Write("ОШИБКА: запустите от имени администратора.");
            return 1;
        }

        var found = PnPGhostScanner.Scan();
        Write($"Найдено записей: {found.Count}");
        foreach (var item in found)
            Write($"  • {item.Detail} — {item.Description}");

        Write("");
        var result = PnPGhostScanner.RemoveAll(Write);
        Write("");
        Write($"Групп дубликатов: {result.GroupsFound}, удалено: {result.Removed}, ошибок: {result.Failed}");
        Write("Рекомендуется перезагрузка Windows.");
        return result.Failed > 0 ? 2 : 0;
    }
}
