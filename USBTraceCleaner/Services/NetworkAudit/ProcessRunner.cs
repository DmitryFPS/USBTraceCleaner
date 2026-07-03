using System.Diagnostics;
using System.Globalization;
using System.Text;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments, int timeoutMs = 60000)
    {
        var preferUtf8 = fileName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
                         || fileName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);
        var encoding = preferUtf8 ? Encoding.UTF8 : GetConsoleEncoding();

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            }
        };

        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit(timeoutMs);
        Task.WaitAll(stdoutTask, stderrTask);

        var output = stdoutTask.Result;
        if (string.IsNullOrWhiteSpace(output))
            output = stderrTask.Result;

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
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wevtutil",
                    Arguments = $"cl \"{channel}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var stdout = proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);
            if (proc.ExitCode == 0)
                return EventLogClearResult.Success;

            var combined = $"{err}\n{stdout}";
            if (EventLogChannelHelper.IsChannelNotFound(combined))
            {
                error = "канал отсутствует в Windows";
                return EventLogClearResult.SkippedNotFound;
            }

            error = string.IsNullOrWhiteSpace(err) ? $"код {proc.ExitCode}" : err.Trim();
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
