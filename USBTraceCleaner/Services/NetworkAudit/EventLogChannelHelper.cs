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
            var result = ProcessExec.Run("wevtutil", $"gl \"{channel}\"", 15_000);
            return result.Ok;
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
