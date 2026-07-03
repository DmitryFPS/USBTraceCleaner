using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal static class EventLogChannelHelper
{
    public static bool Exists(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            return false;

        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wevtutil",
                    Arguments = $"gl \"{channel}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);

            if (proc.ExitCode == 0)
                return true;

            // Канал не найден (RU/EN) или другая ошибка — не добавляем в очистку
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsChannelNotFound(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("channel could not be found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("The specified channel", StringComparison.OrdinalIgnoreCase)
               || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Не удалось найти указанный канал", StringComparison.OrdinalIgnoreCase)
               || message.Contains("не удалось найти указанный канал", StringComparison.OrdinalIgnoreCase)
               || message.Contains("указанный канал", StringComparison.OrdinalIgnoreCase)
               || message.Contains("16000", StringComparison.Ordinal);
    }
}
