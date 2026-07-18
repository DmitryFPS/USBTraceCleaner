using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace USBTraceCleaner.Services;

/// <summary>
/// Безопасный запуск внешних процессов с редиректом stdout/stderr.
/// Избегает классического deadlock ReadToEnd + полного буфера пайпа и зависания
/// Task.WaitAll/Result после таймаута WaitForExit.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ProcessExec
{
    public readonly record struct Result(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public string Combined => $"{StdErr}\n{StdOut}".Trim();
        public bool Ok => !TimedOut && ExitCode == 0;
    }

    public static Result Run(
        string fileName,
        string arguments,
        int timeoutMs = 30_000,
        Encoding? outputEncoding = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (outputEncoding != null)
            {
                psi.StandardOutputEncoding = outputEncoding;
                psi.StandardErrorEncoding = outputEncoding;
            }

            using var proc = new Process { StartInfo = psi };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var sync = new object();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (sync) stdout.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (sync) stderr.AppendLine(e.Data);
            };

            if (!proc.Start())
                return new Result(-1, "", "не удалось запустить процесс", false);

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(timeoutMs))
            {
                TryKill(proc);
                try { proc.WaitForExit(3000); } catch { /* ignore */ }
                string so, se;
                lock (sync)
                {
                    so = stdout.ToString();
                    se = stderr.ToString();
                }

                return new Result(-1, so, string.IsNullOrWhiteSpace(se) ? "таймаут процесса" : se, true);
            }

            // Дождаться завершения асинхронных хендлеров после Exit
            proc.WaitForExit();

            string outText, errText;
            lock (sync)
            {
                outText = stdout.ToString();
                errText = stderr.ToString();
            }

            return new Result(proc.ExitCode, outText, errText, false);
        }
        catch (Exception ex)
        {
            return new Result(-1, "", ex.Message, false);
        }
    }

    /// <summary>Аргументы как список (для PowerShell -Command и т.п.).</summary>
    public static Result Run(
        string fileName,
        IEnumerable<string> argumentList,
        int timeoutMs = 30_000,
        Encoding? outputEncoding = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (outputEncoding != null)
            {
                psi.StandardOutputEncoding = outputEncoding;
                psi.StandardErrorEncoding = outputEncoding;
            }

            foreach (var arg in argumentList)
                psi.ArgumentList.Add(arg);

            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var sync = new object();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (sync) stdout.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (sync) stderr.AppendLine(e.Data);
            };

            if (!proc.Start())
                return new Result(-1, "", "не удалось запустить процесс", false);

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(timeoutMs))
            {
                TryKill(proc);
                try { proc.WaitForExit(3000); } catch { /* ignore */ }
                string so, se;
                lock (sync)
                {
                    so = stdout.ToString();
                    se = stderr.ToString();
                }

                return new Result(-1, so, string.IsNullOrWhiteSpace(se) ? "таймаут процесса" : se, true);
            }

            proc.WaitForExit();
            string outText, errText;
            lock (sync)
            {
                outText = stdout.ToString();
                errText = stderr.ToString();
            }

            return new Result(proc.ExitCode, outText, errText, false);
        }
        catch (Exception ex)
        {
            return new Result(-1, "", ex.Message, false);
        }
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }
}
