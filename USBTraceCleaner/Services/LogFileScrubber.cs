using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace USBTraceCleaner.Services;

/// <summary>
/// Очистка лог-файлов без удаления: содержимое вычищается, даты файла сохраняются.
/// </summary>
public static class LogFileScrubber
{
    private static readonly string[] UsbLinePatterns =
    [
        "USBSTOR", "UASPStor", "USB\\VID_", "USB#VID", "USB#",
        "STORAGE\\RemovableMedia", "STORAGE#RemovableMedia",
        "RemovableMedia", "Ven_General", "Ven_Kingston", "Ven_ASUS",
        "Disk&Ven_", "CdRom&Ven_", "WPDBUSENUM", "WpdMtp",
        "usbstor.inf", "uaspstor.inf", "disk.inf",
        "USB-накопитель", "Mass Storage", "Flash", "UDisk"
    ];

    private static readonly string[] CriticalInfLogs =
    [
        "setupapi.dev.log",
        "setupapi.app.log",
        "setupapi.off.log"
    ];

    public static bool IsManagedLogFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileName(path);
        if (name.StartsWith("setupapi", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("wmiprov.log", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("PcaGeneralDb", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool ScrubOrRestore(string path, bool simulation, bool preserveTimestamps, Action<string>? log = null)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] SCRUB {path}");
            return true;
        }

        if (!File.Exists(path))
            return RestoreMissingLog(path, preserveTimestamps, log);

        var ext = Path.GetExtension(path);
        if (ext.Equals(".ev1", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ev2", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ev3", StringComparison.OrdinalIgnoreCase))
            return TruncatePreserveTimestamps(path, preserveTimestamps, log);

        return ScrubTextLog(path, preserveTimestamps, log);
    }

    /// <summary>Восстанавливает отсутствующие setupapi*.log после прошлой очистки.</summary>
    public static void EnsureCriticalSetupApiLogs(bool simulation, bool preserveTimestamps, Action<string>? log = null)
    {
        var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf");
        foreach (var name in CriticalInfLogs)
        {
            var path = Path.Combine(infDir, name);
            if (!File.Exists(path))
                RestoreMissingLog(path, preserveTimestamps, log, simulation);
        }
    }

    public static int ScrubSetupApiContent(ReadOnlySpan<char> input, StringBuilder output)
    {
        var removedLines = 0;
        var skipBlock = false;
        var lines = input.ToString().Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsBlockEnd(line))
            {
                if (skipBlock)
                {
                    skipBlock = false;
                    removedLines++;
                    continue;
                }
            }

            if (skipBlock)
            {
                removedLines++;
                continue;
            }

            if (ShouldRemoveLine(line))
            {
                removedLines++;
                if (IsBlockStart(line))
                    skipBlock = true;
                continue;
            }

            output.AppendLine(line);
        }

        return removedLines;
    }

    private static bool ScrubTextLog(string path, bool preserveTimestamps, Action<string>? log)
    {
        try
        {
            var timestamps = preserveTimestamps ? CaptureTimestamps(path) : null;
            var original = File.ReadAllText(path, Encoding.Default);
            var builder = new StringBuilder(original.Length);
            var removed = ScrubSetupApiContent(original, builder);
            var cleaned = builder.ToString();

            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = MinimalSetupApiHeader();

            WriteWithTimestamps(path, cleaned, timestamps, preserveTimestamps);
            log?.Invoke($"[OK]  SCRUB {path} (удалено строк: {removed}, даты сохранены)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[FAIL] SCRUB {path}: {ex.Message}");
            return false;
        }
    }

    private static bool TruncatePreserveTimestamps(string path, bool preserveTimestamps, Action<string>? log)
    {
        try
        {
            var timestamps = preserveTimestamps ? CaptureTimestamps(path) : null;
            WriteWithTimestamps(path, Array.Empty<byte>(), timestamps, preserveTimestamps);
            log?.Invoke($"[OK]  SCRUB {path} (бинарный журнал обнулён, даты сохранены)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[FAIL] SCRUB {path}: {ex.Message}");
            return false;
        }
    }

    private static bool RestoreMissingLog(
        string path,
        bool preserveTimestamps,
        Action<string>? log,
        bool simulation = false)
    {
        if (simulation)
        {
            log?.Invoke($"[SIM] RESTORE {path}");
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var timestamps = preserveTimestamps ? CreatePlausibleTimestamps(path) : null;
            var content = Path.GetFileName(path).StartsWith("setupapi", StringComparison.OrdinalIgnoreCase)
                ? MinimalSetupApiHeader()
                : string.Empty;
            WriteWithTimestamps(path, content, timestamps, preserveTimestamps);
            log?.Invoke($"[OK]  RESTORE {path} (файл воссоздан, старые даты)");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[FAIL] RESTORE {path}: {ex.Message}");
            return false;
        }
    }

    private static string MinimalSetupApiHeader() =>
        "[Device Install Log]\r\n";

    private static bool ShouldRemoveLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        foreach (var pattern in UsbLinePatterns)
        {
            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsBlockStart(string line) =>
        line.Contains(">>>", StringComparison.Ordinal)
        || line.Contains("[Device Install", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockEnd(string line) =>
        line.Contains("<<<", StringComparison.Ordinal)
        || line.Trim().Length == 0;

    private sealed class FileTimestamps
    {
        public DateTime CreationUtc { get; init; }
        public DateTime LastWriteUtc { get; init; }
        public DateTime LastAccessUtc { get; init; }
    }

    private static FileTimestamps CaptureTimestamps(string path)
    {
        var info = new FileInfo(path);
        return new FileTimestamps
        {
            CreationUtc = info.CreationTimeUtc,
            LastWriteUtc = info.LastWriteTimeUtc,
            LastAccessUtc = info.LastAccessTimeUtc
        };
    }

    private static FileTimestamps CreatePlausibleTimestamps(string targetPath)
    {
        var install = GetWindowsInstallDateUtc();
        if (install.HasValue)
        {
            return new FileTimestamps
            {
                CreationUtc = install.Value,
                LastWriteUtc = install.Value.AddHours(2),
                LastAccessUtc = install.Value.AddHours(2)
            };
        }

        var infDir = Path.GetDirectoryName(targetPath)!;
        DateTime? oldest = null;
        if (Directory.Exists(infDir))
        {
            foreach (var file in Directory.EnumerateFiles(infDir, "*.inf").Take(50))
            {
                var write = new FileInfo(file).LastWriteTimeUtc;
                oldest = oldest == null || write < oldest ? write : oldest;
            }
        }

        var fallback = oldest ?? DateTime.UtcNow.AddYears(-1);
        return new FileTimestamps
        {
            CreationUtc = fallback,
            LastWriteUtc = fallback,
            LastAccessUtc = fallback
        };
    }

    private static DateTime? GetWindowsInstallDateUtc()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var raw = key?.GetValue("InstallDate");
            if (raw is int unixSeconds)
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        }
        catch { /* ignore */ }
        return null;
    }

    private static void WriteWithTimestamps(string path, string content, FileTimestamps? timestamps, bool preserve)
    {
        var bytes = Encoding.Default.GetBytes(content);
        WriteWithTimestamps(path, bytes, timestamps, preserve);
    }

    private static void WriteWithTimestamps(string path, byte[] content, FileTimestamps? timestamps, bool preserve)
    {
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            fs.Write(content);
            fs.Flush(true);
        }

        if (preserve && timestamps != null)
            ApplyTimestamps(path, timestamps);
    }

    private static void ApplyTimestamps(string path, FileTimestamps timestamps)
    {
        try
        {
            File.SetCreationTimeUtc(path, timestamps.CreationUtc);
            File.SetLastWriteTimeUtc(path, timestamps.LastWriteUtc);
            File.SetLastAccessTimeUtc(path, timestamps.LastAccessUtc);
        }
        catch
        {
            NativeSetFileTime(path, timestamps);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileTime(IntPtr hFile, ref long creation, ref long lastAccess, ref long lastWrite);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static void NativeSetFileTime(string path, FileTimestamps ts)
    {
        const uint genericWrite = 0x40000000;
        const uint openExisting = 3;
        const uint flagBackupSemantics = 0x02000000;

        var handle = CreateFileW(path, genericWrite, 0, IntPtr.Zero, openExisting, flagBackupSemantics, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

        try
        {
            long c = ts.CreationUtc.ToFileTimeUtc();
            long a = ts.LastAccessUtc.ToFileTimeUtc();
            long w = ts.LastWriteUtc.ToFileTimeUtc();
            SetFileTime(handle, ref c, ref a, ref w);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
