using System.Text;
using System.Windows;
using System.Diagnostics.CodeAnalysis;
using USBTraceCleaner.Services;

namespace USBTraceCleaner;

[ExcludeFromCodeCoverage]
public partial class App : Application
{
    static App()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--self-test"))
        {
            Environment.Exit(SelfTestRunner.Run());
        }

        if (e.Args.Contains("--clean"))
        {
            Environment.Exit(HeadlessCleaner.Run());
        }

        if (e.Args.Contains("--restore-logs"))
        {
            Environment.Exit(LogRestoreRunner.Run());
        }

        if (e.Args.Contains("--fix-duplicates"))
        {
            Environment.Exit(GhostInstanceCleanerRunner.Run());
        }

        var window = new MainWindow();
        window.Show();
        base.OnStartup(e);
    }
}
