using USBTraceCleaner.Models;

namespace USBTraceCleaner.Tests;

public class NetworkAuditSummaryModelsTests
{
    [Fact]
    public void ToPlainText_EmptySections()
    {
        var summary = new NetworkAuditReadableSummary();
        Assert.Equal("Сводка появится после сканирования.", summary.ToPlainText());
    }

    [Fact]
    public void ToPlainText_FormatsSections()
    {
        var summary = new NetworkAuditReadableSummary
        {
            Sections =
            [
                new NetworkAuditSummarySection
                {
                    Title = "Обзор",
                    Hint = "подсказка",
                    Lines = ["строка 1", "строка 2"]
                }
            ]
        };

        var text = summary.ToPlainText();
        Assert.Contains("Обзор", text);
        Assert.Contains("подсказка", text);
        Assert.Contains("строка 1", text);
    }
}
