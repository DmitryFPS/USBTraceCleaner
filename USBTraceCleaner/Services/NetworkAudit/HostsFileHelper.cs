using System.IO;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services.NetworkAudit;

[ExcludeFromCodeCoverage]
internal static class HostsFileHelper
{
    public const string DefaultContent = """
        # Copyright (c) 1993-2009 Microsoft Corp.
        #
        # This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
        #
        127.0.0.1       localhost
        ::1             localhost

        """;

    public static (bool Ok, string? Error) ResetToDefault(string path, Action<string> log)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    log($"  — hosts: повторная попытка {attempt}/3…");
                    Thread.Sleep(400);
                }

                UnlockForAdministrators(path, log);
                File.WriteAllText(path, DefaultContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var written = File.ReadAllText(path);
                if (written.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                    written.Contains("kubernetes", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Docker Desktop перезаписал hosts — закройте Docker и Kaspersky, затем повторите");
                }

                log("  ✓ Файл hosts сброшен (Docker/Kubernetes записи удалены)");
                log("  ⚠ Docker Desktop при запуске может добавить записи снова");
                return (true, null);
            }
            catch (UnauthorizedAccessException)
            {
                UnlockForAdministrators(path, log);
                if (attempt == 3)
                    return (false, "нет доступа к hosts — закройте Docker Desktop и Kaspersky (защита hosts)");
            }
            catch (IOException ex) when (attempt < 3)
            {
                log($"  — hosts занят: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        return (false, "не удалось записать hosts после 3 попыток");
    }

    private static void UnlockForAdministrators(string path, Action<string> log)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch { /* ignore */ }

        try
        {
            ProcessRunner.Run("takeown", $"/f \"{path}\" /a", timeoutMs: 15000);
        }
        catch { /* ignore */ }

        try
        {
            ProcessRunner.Run("icacls", $"\"{path}\" /grant Administrators:F /c", timeoutMs: 15000);
        }
        catch { /* ignore */ }

        try
        {
            ProcessRunner.Run("icacls", $"\"{path}\" /grant *S-1-5-32-544:F /c", timeoutMs: 15000);
        }
        catch { /* ignore */ }
    }
}
