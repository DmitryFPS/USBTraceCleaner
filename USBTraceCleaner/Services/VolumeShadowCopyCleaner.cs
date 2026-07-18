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
            var result = ProcessExec.Run("vssadmin.exe", "delete shadows /all /quiet", 120_000);
            if (result.Ok)
                log?.Invoke("[OK]  VSS shadows удалены");
            else if (result.TimedOut)
                log?.Invoke("[WARN] vssadmin: таймаут");
            else
                log?.Invoke($"[WARN] vssadmin exit={result.ExitCode}: {Trim(result.Combined)}");
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
