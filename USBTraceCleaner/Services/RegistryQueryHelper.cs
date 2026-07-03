using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Services;

[ExcludeFromCodeCoverage]
public static class RegistryQueryHelper
{
    public static IEnumerable<string> EnumerateSubKeyPaths(RegistryHive hive, string subKey)
    {
        foreach (var path in EnumerateAllKeyPaths(hive, subKey))
        {
            if (!path.Equals(subKey, StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    /// <summary>Непосредственные дочерние ключи (один уровень).</summary>
    public static List<string> EnumerateImmediateSubKeyPaths(RegistryHive hive, string subKey)
    {
        var prefix = GetPrefix(hive);
        var hivePrefix = GetHivePrefix(hive);
        var fullPath = $"{prefix}\\{subKey}";

        var psi = new ProcessStartInfo("reg.exe", $"query \"{fullPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default
        };

        using var proc = Process.Start(psi);
        if (proc == null) return [];

        var output = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(30000))
        {
            try { proc.Kill(true); } catch { }
            return [];
        }

        return ParseImmediateChildrenFromRegQuery(output, hivePrefix, subKey);
    }

    /// <summary>
    /// Все ключи под деревом (включая корень), отсортированы от глубоких к мелким.
    /// </summary>
    public static List<string> EnumerateAllKeyPaths(RegistryHive hive, string subKey)
    {
        var prefix = GetPrefix(hive);
        var hivePrefix = GetHivePrefix(hive);
        var fullPath = $"{prefix}\\{subKey}";

        var psi = new ProcessStartInfo("reg.exe", $"query \"{fullPath}\" /s")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default
        };

        using var proc = Process.Start(psi);
        if (proc == null) return [subKey];

        var output = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(60000))
        {
            try { proc.Kill(true); } catch { }
            return [subKey];
        }

        return ParseKeyPathsFromRegQuery(output, hivePrefix, subKey);
    }

    private static List<string> ParseKeyPathsFromRegQuery(string output, string hivePrefix, string subKey)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { subKey };
        var pending = new StringBuilder();

        foreach (var rawLine in output.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(hivePrefix, StringComparison.OrdinalIgnoreCase))
            {
                FlushRegQueryPath(pending, hivePrefix, subKey, paths);
                pending.Clear();
                pending.Append(line);
            }
            else if (pending.Length > 0 && !line.Contains("REG_", StringComparison.OrdinalIgnoreCase))
            {
                // reg.exe переносит длинные пути (например ...\Properties) на следующую строку
                pending.Append(line);
            }
        }

        FlushRegQueryPath(pending, hivePrefix, subKey, paths);
        return paths.OrderByDescending(p => p.Length).ToList();
    }

    private static void FlushRegQueryPath(StringBuilder pending, string hivePrefix, string subKey, HashSet<string> paths)
    {
        if (pending.Length == 0) return;

        var trimmed = pending.ToString().Trim();
        if (!trimmed.StartsWith(hivePrefix, StringComparison.OrdinalIgnoreCase))
            return;
        if (trimmed.Contains("REG_", StringComparison.OrdinalIgnoreCase))
            return;

        var relative = trimmed[hivePrefix.Length..];
        if (relative.Equals(subKey, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith(subKey + "\\", StringComparison.OrdinalIgnoreCase))
        {
            paths.Add(relative);
        }
    }

    private static List<string> ParseImmediateChildrenFromRegQuery(string output, string hivePrefix, string subKey)
    {
        var children = new List<string>();
        var pending = new StringBuilder();

        foreach (var rawLine in output.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(hivePrefix, StringComparison.OrdinalIgnoreCase))
            {
                FlushImmediateChild(pending, hivePrefix, subKey, children);
                pending.Clear();
                pending.Append(line);
            }
            else if (pending.Length > 0 && !line.Contains("REG_", StringComparison.OrdinalIgnoreCase))
            {
                pending.Append(line);
            }
        }

        FlushImmediateChild(pending, hivePrefix, subKey, children);
        return children;
    }

    private static void FlushImmediateChild(StringBuilder pending, string hivePrefix, string subKey, List<string> children)
    {
        if (pending.Length == 0) return;

        var trimmed = pending.ToString().Trim();
        if (!trimmed.StartsWith(hivePrefix, StringComparison.OrdinalIgnoreCase))
            return;
        if (trimmed.Contains("REG_", StringComparison.OrdinalIgnoreCase))
            return;

        var relative = trimmed[hivePrefix.Length..];
        if (relative.Equals(subKey, StringComparison.OrdinalIgnoreCase))
            return;
        if (relative.StartsWith(subKey + "\\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = relative[(subKey.Length + 1)..];
            if (!suffix.Contains('\\'))
                children.Add(suffix);
        }
    }

    private static string GetPrefix(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.Users => "HKU",
        _ => "HKLM"
    };

    private static string GetHivePrefix(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => @"HKEY_LOCAL_MACHINE\",
        RegistryHive.CurrentUser => @"HKEY_CURRENT_USER\",
        RegistryHive.Users => @"HKEY_USERS\",
        _ => @"HKEY_LOCAL_MACHINE\"
    };
}
