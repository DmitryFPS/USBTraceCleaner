using System.IO;
using System.Text;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Services;

/// <summary>
/// Headless self-test runner: USBTraceCleaner.exe --self-test
/// </summary>
public static class SelfTestRunner
{
    public static int Run()
    {
        var log = new StringBuilder();
        var failed = 0;

        void Pass(string name) => log.AppendLine($"  OK  {name}");
        void Fail(string name, string detail)
        {
            log.AppendLine($"  FAIL {name}: {detail}");
            failed++;
        }
        void Info(string line) => log.AppendLine(line);

        Info("USB Trace Cleaner — Self Test");
        Info($"Admin: {AdminHelper.IsAdministrator()} | OS: {AdminHelper.GetWindowsVersionLabel()}");
        Info("");

        // 1. Export root
        try
        {
            var root = RegistryExportHelper.GetExportRoot(@"SYSTEM\ControlSet001\Enum\USBSTOR\Disk");
            if (root.EndsWith(@"Enum\USBSTOR"))
                Pass("GetExportRoot USBSTOR");
            else
                Fail("GetExportRoot USBSTOR", root);
        }
        catch (Exception ex) { Fail("GetExportRoot USBSTOR", ex.Message); }

        // 2. App launch prerequisites
        if (AdminHelper.IsWindows10Or11())
            Pass("Windows version check");
        else
            Fail("Windows version check", "Not Win10/11");

        if (!AdminHelper.IsAdministrator())
        {
            Info("\nNot running as admin — skipping registry tests");
            WriteLog(log);
            return failed;
        }

        // 3. Test key create/delete
        const string testRoot = @"SOFTWARE\USBTraceCleanerSelfTest";
        try
        {
            RegistryHelper.DeleteKey(Microsoft.Win32.RegistryHive.LocalMachine, testRoot, false);
            using var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(testRoot);
            k!.CreateSubKey("A")!.CreateSubKey("B");
            if (!RegistryHelper.KeyExists(Microsoft.Win32.RegistryHive.LocalMachine, $@"{testRoot}\A\B"))
                Fail("Create test key", "key missing");
            else
            {
                var ok = RegistryHelper.DeleteKey(Microsoft.Win32.RegistryHive.LocalMachine, testRoot, false);
                if (ok && !RegistryHelper.KeyExists(Microsoft.Win32.RegistryHive.LocalMachine, testRoot))
                    Pass("DeleteKey recursive");
                else
                    Fail("DeleteKey recursive", "key still exists");
            }
        }
        catch (Exception ex) { Fail("DeleteKey recursive", ex.Message); }

        // 4. Scan
        try
        {
            var scanner = new ArtifactScanner();
            var items = scanner.Scan(new CleanupOptions());
            if (items.Count > 0)
                Pass($"Scan found {items.Count} artifacts");
            else
                Fail("Scan", "0 artifacts");
        }
        catch (Exception ex) { Fail("Scan", ex.Message); }

        // 5. Simulation clean
        try
        {
            var before = RegistryHelper.CountUsbStorDevices();
            var scanner = new ArtifactScanner();
            var cleaner = new ArtifactCleaner();
            var items = scanner.Scan(new CleanupOptions());
            var result = cleaner.ExecuteAsync(items, new CleanupOptions
            {
                SimulationMode = true,
                SaveBackup = false,
                CreateRestorePoint = false,
                CloseExplorer = false,
                RebootAfterClean = false
            }).GetAwaiter().GetResult();
            var after = RegistryHelper.CountUsbStorDevices();
            if (result.Success && before == after)
                Pass("Simulation clean (no changes)");
            else
                Fail("Simulation clean", $"before={before} after={after}");
        }
        catch (Exception ex) { Fail("Simulation clean", ex.Message); }

        // 6. Real clean USBSTOR (если безопасно)
        try
        {
            var before = RegistryHelper.CountUsbDeviewDevices();
            if (before == 0)
            {
                Info("  ~ Real clean skipped (USBSTOR already empty)");
            }
            else
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "USBTC_selftest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var scanner = new ArtifactScanner();
                var cleaner = new ArtifactCleaner();
                var items = scanner.Scan(new CleanupOptions { CleanAllUsbDevices = false });
                var result = cleaner.ExecuteAsync(items, new CleanupOptions
                {
                    SimulationMode = false,
                    SaveBackup = true,
                    BackupPath = tempDir,
                    CreateRestorePoint = false,
                    CloseExplorer = false,
                    RebootAfterClean = false,
                    CleanAllUsbDevices = true,
                    ExportFullUsbEnum = false,
                    CleanEventLogs = false
                }).GetAwaiter().GetResult();

                var after = RegistryHelper.CountUsbDeviewDevices();
                if (after == 0)
                    Pass($"Real clean USB ({before} → 0, USBDeview)");
                else
                    Fail("Real clean USB", $"remaining={after} failed={result.FailedCount}");

                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
        catch (Exception ex) { Fail("Real clean USBSTOR", ex.Message); }

        Info("");
        Info(failed == 0 ? "ALL TESTS PASSED" : $"FAILED: {failed}");
        WriteLog(log);
        return failed == 0 ? 0 : 1;
    }

    private static void WriteLog(StringBuilder log)
    {
        var path = Path.Combine(Path.GetTempPath(), "USBTraceCleaner_selftest.log");
        File.WriteAllText(path, log.ToString(), System.Text.Encoding.UTF8);
        try { Console.WriteLine(File.ReadAllText(path, System.Text.Encoding.UTF8)); } catch { }
    }
}
