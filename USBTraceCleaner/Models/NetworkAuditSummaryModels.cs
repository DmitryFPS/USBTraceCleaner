namespace USBTraceCleaner.Models;

public sealed class NetworkAuditSummarySection
{
    public required string Title { get; init; }
    public string? Hint { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];
    public bool IsAttention { get; init; }
}

public sealed class NetworkAuditReadableSummary
{
    public IReadOnlyList<NetworkAuditSummarySection> Sections { get; init; } = [];

    public string ToPlainText()
    {
        if (Sections.Count == 0)
            return "Сводка появится после сканирования.";

        var sb = new System.Text.StringBuilder();
        foreach (var section in Sections)
        {
            sb.AppendLine(section.Title);
            if (!string.IsNullOrWhiteSpace(section.Hint))
                sb.AppendLine($"  ({section.Hint})");
            foreach (var line in section.Lines)
                sb.AppendLine($"  • {line}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
