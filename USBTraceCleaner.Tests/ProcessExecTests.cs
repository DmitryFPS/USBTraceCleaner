using USBTraceCleaner.Services;

namespace USBTraceCleaner.Tests;

public class ProcessExecTests
{
    [Fact]
    public void Run_CmdEcho_SucceedsWithoutHang()
    {
        var result = ProcessExec.Run("cmd.exe", "/c echo hello-deadlock-check", 10_000);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-deadlock-check", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_TimeoutKillsProcess()
    {
        var result = ProcessExec.Run("cmd.exe", "/c ping -n 30 127.0.0.1 >nul", 800);
        Assert.True(result.TimedOut);
    }
}
