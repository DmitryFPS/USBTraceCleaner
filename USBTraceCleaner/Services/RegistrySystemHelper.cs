using System.Diagnostics;
using System.IO;
using System.Text;

namespace USBTraceCleaner.Services;

/// <summary>
/// Выполнение reg delete от имени SYSTEM (без cmd.exe — корректно для путей с &amp;).
/// </summary>
public static class RegistrySystemHelper
{
    private static readonly string SystemTemp = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

    public static bool TryRegDeleteAsSystem(string hivePrefix, string subKey) =>
        TryBatchRegDeleteAsSystem([$"{hivePrefix}\\{subKey}"]);

    public static bool TryBatchRegDeleteAsSystem(IEnumerable<string> fullPaths)
    {
        var paths = fullPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return true;

        var taskId = Guid.NewGuid().ToString("N");
        var taskName = "USBTC_" + taskId;
        var scriptPath = Path.Combine(SystemTemp, taskName + ".ps1");
        var exitFile = Path.Combine(SystemTemp, taskName + ".exit");

        try
        {
            if (File.Exists(exitFile)) File.Delete(exitFile);

            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            script.AppendLine("$err = 0");
            foreach (var path in paths.OrderByDescending(p => p.Length))
            {
                var quoted = path.Replace("'", "''");
                script.AppendLine($"& reg.exe delete '{quoted}' /f | Out-Null");
                script.AppendLine("if ($LASTEXITCODE -ne 0) { $err = 1 }");
            }
            script.AppendLine($"Set-Content -LiteralPath '{exitFile.Replace("'", "''")}' -Value $err -Encoding ASCII");
            File.WriteAllText(scriptPath, script.ToString(), Encoding.UTF8);

            var tr = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"";
            var create = Run("schtasks.exe",
                $"/create /tn \"{taskName}\" /tr \"{tr}\" /sc ONCE /sd 01/01/2099 /st 00:00 /ru SYSTEM /rl HIGHEST /f");
            if (create != 0) return false;

            if (Run("schtasks.exe", $"/run /tn \"{taskName}\"") != 0)
                return false;

            for (var i = 0; i < 120; i++)
            {
                if (File.Exists(exitFile))
                    break;
                Thread.Sleep(500);
            }

            if (!File.Exists(exitFile)) return false;
            return File.ReadAllText(exitFile).Trim() == "0";
        }
        finally
        {
            Run("schtasks.exe", $"/delete /tn \"{taskName}\" /f");
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            try { if (File.Exists(exitFile)) File.Delete(exitFile); } catch { }
        }
    }

    private static int Run(string file, string arguments)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return -1;
        if (!proc.WaitForExit(30000))
        {
            try { proc.Kill(true); } catch { }
            return -1;
        }
        return proc.ExitCode;
    }
}
