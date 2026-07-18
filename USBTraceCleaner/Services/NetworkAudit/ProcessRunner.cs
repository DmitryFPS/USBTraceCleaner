using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using USBTraceCleaner.Models;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments, int timeoutMs = 60000)
    {
        var preferUtf8 = fileName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
                         || fileName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);
        var encoding = preferUtf8 ? Encoding.UTF8 : GetConsoleEncoding();

        var result = ProcessExec.Run(fileName, arguments, timeoutMs, encoding);
        var output = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        if (result.TimedOut && string.IsNullOrWhiteSpace(output))
            output = "таймаут процесса";

        return NetworkAuditDisplay.SanitizeForDisplay(output);
    }

    public enum EventLogClearResult
    {
        Success,
        SkippedNotFound,
        Failed
    }

    public static EventLogClearResult TryClearEventLog(string channel, out string? error)
    {
        error = null;
        if (!EventLogChannelHelper.Exists(channel))
        {
            error = "канал отсутствует в Windows";
            return EventLogClearResult.SkippedNotFound;
        }

        try
        {
            var result = ProcessExec.Run("wevtutil", $"cl \"{channel}\"", 30_000);
            if (result.Ok)
                return EventLogClearResult.Success;

            if (EventLogChannelHelper.IsChannelNotFound(result.Combined))
            {
                error = "канал отсутствует в Windows";
                return EventLogClearResult.SkippedNotFound;
            }

            error = result.TimedOut
                ? "таймаут wevtutil"
                : string.IsNullOrWhiteSpace(result.StdErr)
                    ? $"код {result.ExitCode}"
                    : result.StdErr.Trim();
            return EventLogClearResult.Failed;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return EventLogClearResult.Failed;
        }
    }

    private static Encoding GetConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            try { return Encoding.GetEncoding(866); }
            catch (ArgumentException) { return Encoding.GetEncoding(1251); }
        }
    }
}
