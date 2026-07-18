using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class VolumeShadowCopyCleaner
{
    public static void DeleteAllShadows(bool simulation, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke("[SIM] vssadmin delete shadows /all /quiet");
            return;
        }

        log?.Invoke("--- Удаление Volume Shadow Copies (VSS) ---");
        try
        {
            var psi = new ProcessStartInfo("vssadmin.exe", "delete shadows /all /quiet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                log?.Invoke("[FAIL] vssadmin не запустился");
                return;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);
            if (proc.ExitCode == 0)
                log?.Invoke("[OK]  VSS shadows удалены");
            else
                log?.Invoke($"[WARN] vssadmin exit={proc.ExitCode}: {Trim(stdout + stderr)}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[WARN] VSS: {ex.Message}");
        }
    }

    private static string Trim(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 200 ? s[..200] + "…" : s;
    }
}
