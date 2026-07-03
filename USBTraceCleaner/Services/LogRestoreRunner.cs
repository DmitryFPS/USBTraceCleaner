using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class LogRestoreRunner
{
    public static int Run()
    {
        if (!AdminHelper.IsAdministrator())
        {
            Console.Error.WriteLine("Требуются права администратора.");
            return 1;
        }

        LogFileScrubber.EnsureCriticalSetupApiLogs(false, preserveTimestamps: true, log: Console.WriteLine);
        Console.WriteLine("Готово. Перезапустите USBDetector.");
        return 0;
    }
}
