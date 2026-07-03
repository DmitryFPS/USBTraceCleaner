using System.Windows;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class LogWindow : Window
{
    public LogWindow(string title, string logText)
    {
        InitializeComponent();
        Title = title;
        TxtLogContent.Text = logText;
        TxtLogContent.CaretIndex = TxtLogContent.Text.Length;
        TxtLogContent.ScrollToEnd();
    }

    public static void ShowDialog(Window owner, string title, string logText)
    {
        var window = new LogWindow(title, logText) { Owner = owner };
        window.ShowDialog();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtLogContent.Text))
            Clipboard.SetText(TxtLogContent.Text);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
