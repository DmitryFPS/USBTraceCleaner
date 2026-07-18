namespace USBTraceCleaner.Tests;

public class EngineeringGuideTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "USBTraceCleaner.sln");
            if (File.Exists(sln))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("USBTraceCleaner.sln not found from test base directory.");
    }

    private static string DocsDir => Path.Combine(FindRepoRoot(), "docs");

    [Fact]
    public void EngineeringGuide_HtmlAndPdf_ExistInDocs()
    {
        var html = Directory.GetFiles(DocsDir, "USBTraceCleaner_*.html").FirstOrDefault();
        var pdf = Directory.GetFiles(DocsDir, "USBTraceCleaner_*.pdf").FirstOrDefault();

        Assert.NotNull(html);
        Assert.NotNull(pdf);
        Assert.True(new FileInfo(pdf!).Length > 1000);

        var sig = File.ReadAllBytes(pdf).AsSpan(0, 4).ToArray();
        Assert.Equal("%PDF"u8.ToArray(), sig);
    }

    [Fact]
    public void EngineeringGuide_Html_HasNoKnownDiscrepanciesSection()
    {
        var html = Directory.GetFiles(DocsDir, "USBTraceCleaner_*.html").Single();
        var text = File.ReadAllText(html);
        Assert.Contains("USBTraceCleaner", text);
        Assert.DoesNotContain("Известные расхождения текущей версии", text);
        Assert.DoesNotContain("GUI всегда передаёт", text);
    }

    [Fact]
    public void EngineeringGuide_Html_DocumentsSimulationAndWhitelistProtection()
    {
        var html = Directory.GetFiles(DocsDir, "USBTraceCleaner_*.html").Single();
        var text = File.ReadAllText(html);
        Assert.Contains("Симуляция (без удаления)", text);
        Assert.Contains("исключает их из очистки", text);
        Assert.Contains("build-exe.ps1", text);
    }

    [Fact]
    public void Readme_VersionAndBuildGuide_AreCurrent()
    {
        var readme = Path.Combine(FindRepoRoot(), "README.md");
        var text = File.ReadAllText(readme);
        Assert.Contains("**Версия:** 1.7.1", text);
        Assert.Contains("build-exe.ps1", text);
        Assert.Contains("Инженерное руководство", text);
        Assert.Contains("не удаляются", text);
        Assert.DoesNotContain("**Версия:** 1.7.0", text);
    }
}
