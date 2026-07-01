using System.Diagnostics;
using System.Text;

namespace USBTraceCleaner.Services.NetworkAudit;

internal static class ProcessRunner
{
    public static string Run(string fileName, string arguments, int timeoutMs = 60000)
    {
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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        _ = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeoutMs);
        return output;
    }
}
