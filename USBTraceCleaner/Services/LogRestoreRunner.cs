namespace USBTraceCleaner.Services;

/// <summary>USBTraceCleaner.exe --restore-logs — восстановить setupapi.dev.log без полной очистки.</summary>
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
