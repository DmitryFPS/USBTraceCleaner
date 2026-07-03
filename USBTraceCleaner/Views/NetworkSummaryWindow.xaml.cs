using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using USBTraceCleaner.Models;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class NetworkSummaryWindow : Window
{
    private readonly NetworkAuditReadableSummary _summary;

    public NetworkSummaryWindow(NetworkAuditReadableSummary summary)
    {
        InitializeComponent();
        _summary = summary;
        RenderSections();
    }

    public static void ShowDialog(Window owner, NetworkAuditReadableSummary summary)
    {
        var window = new NetworkSummaryWindow(summary) { Owner = owner };
        window.ShowDialog();
    }

    private void RenderSections()
    {
        SectionsPanel.Children.Clear();

        foreach (var section in _summary.Sections)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            var titleBrush = section.IsAttention
                ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                : (Brush)FindResource("TextPrimaryBrush");

            block.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = titleBrush,
                Margin = new Thickness(8, 8, 8, 4)
            });

            if (!string.IsNullOrWhiteSpace(section.Hint))
            {
                block.Children.Add(new TextBlock
                {
                    Text = section.Hint,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 0, 8, 6)
                });
            }

            foreach (var line in section.Lines)
            {
                block.Children.Add(new TextBlock
                {
                    Text = $"• {line}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    Foreground = section.IsAttention
                        ? new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B))
                        : (Brush)FindResource("TextPrimaryBrush"),
                    Margin = new Thickness(8, 0, 8, 3)
                });
            }

            SectionsPanel.Children.Add(block);
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = _summary.ToPlainText();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
