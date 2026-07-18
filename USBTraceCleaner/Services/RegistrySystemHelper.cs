using System.Diagnostics;
using System.IO;
using System.Text;

using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
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

        // До 3 попыток: WPDBUSENUM часто отдаёт Access Denied с первого раза
        var ok = false;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ok = RunSystemDeleteOnce(paths);
            if (ok) break;
            Thread.Sleep(750 * attempt);
        }
        return ok;
    }

    private static bool RunSystemDeleteOnce(List<string> paths)
    {
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
                script.AppendLine("if ($LASTEXITCODE -ne 0) { Start-Sleep -Milliseconds 200; & reg.exe delete '{quoted}' /f | Out-Null }");
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
        var result = ProcessExec.Run(file, arguments, 30_000);
        return result.TimedOut ? -1 : result.ExitCode;
    }
}
