using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace USBTraceCleaner.Controls;

/// <summary>
/// Results DataGrid helpers: last-column auto-width, text wrap support, Shift+wheel,
/// and copying selected rows to the clipboard (Ctrl+C / context menu).
/// </summary>
public static class DataGridScrollHelper
{
    private const double CellPadding = 28;
    private const double MaxColumnWidth = 720;

    private static readonly ConditionalWeakTable<DataGrid, NotifyCollectionChangedEventHandler> ItemHandlers = new();

    public static readonly DependencyProperty FillAndScrollProperty =
        DependencyProperty.RegisterAttached(
            "FillAndScroll",
            typeof(bool),
            typeof(DataGridScrollHelper),
            new PropertyMetadata(false, OnChanged));

    public static void SetFillAndScroll(DependencyObject element, bool value) =>
        element.SetValue(FillAndScrollProperty, value);

    public static bool GetFillAndScroll(DependencyObject element) =>
        (bool)element.GetValue(FillAndScrollProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        if (e.NewValue is true)
        {
            grid.Loaded += OnLoaded;
            grid.IsVisibleChanged += OnIsVisibleChanged;
            grid.PreviewMouseWheel += OnPreviewMouseWheel;
            grid.PreviewKeyDown += OnPreviewKeyDown;
            HookItems(grid);
            if (grid.IsLoaded)
                OnLoaded(grid, new RoutedEventArgs());
        }
        else
        {
            grid.Loaded -= OnLoaded;
            grid.IsVisibleChanged -= OnIsVisibleChanged;
            grid.PreviewMouseWheel -= OnPreviewMouseWheel;
            grid.PreviewKeyDown -= OnPreviewKeyDown;
            UnhookItems(grid);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        grid.ClearValue(FrameworkElement.WidthProperty);
        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        // Cell selection allows copying individual cells; row header still selects the whole row.
        grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        EnsureContextMenu(grid);
        HookItems(grid);
        ScheduleSizeLastColumn(grid);
    }

    private static void EnsureContextMenu(DataGrid grid)
    {
        var menu = new ContextMenu();

        var copyCell = new MenuItem { Header = "Копировать ячейку\tCtrl+Shift+C" };
        copyCell.Click += (_, _) => CopySelectedCells(grid);

        var copyRows = new MenuItem { Header = "Копировать строки с заголовками\tCtrl+C" };
        copyRows.Click += (_, _) => CopySelectedRows(grid, includeHeaders: true);

        var copyRowsNoHeaders = new MenuItem { Header = "Копировать строки без заголовков" };
        copyRowsNoHeaders.Click += (_, _) => CopySelectedRows(grid, includeHeaders: false);

        menu.Items.Add(copyCell);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyRows);
        menu.Items.Add(copyRowsNoHeaders);
        grid.ContextMenu = menu;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (CopySelectedCells(grid))
                e.Handled = true;
            return;
        }

        if (CopySelectedRows(grid, includeHeaders: true))
            e.Handled = true;
    }

    /// <summary>
    /// Copies only selected cell values (or the current cell). Multi-cell selection is TSV.
    /// </summary>
    public static bool CopySelectedCells(DataGrid grid)
    {
        var cells = grid.SelectedCells
            .Where(c => c.Item is not null &&
                        !ReferenceEquals(c.Item, CollectionView.NewItemPlaceholder) &&
                        c.Column is not null)
            .ToList();

        if (cells.Count == 0 &&
            grid.CurrentCell.Item is not null &&
            grid.CurrentCell.Column is not null &&
            !ReferenceEquals(grid.CurrentCell.Item, CollectionView.NewItemPlaceholder))
        {
            cells.Add(grid.CurrentCell);
        }

        if (cells.Count == 0)
            return false;

        // Single cell → plain text (no tabs/newlines).
        if (cells.Count == 1)
        {
            var cell = cells[0];
            return SetClipboardText(GetColumnText(cell.Column, cell.Item));
        }

        var sb = new StringBuilder();
        var rowGroups = cells
            .GroupBy(c => c.Item)
            .OrderBy(g => grid.Items.IndexOf(g.Key));

        foreach (var rowGroup in rowGroups)
        {
            var ordered = rowGroup.OrderBy(c => c.Column.DisplayIndex);
            sb.AppendLine(string.Join('\t', ordered.Select(c => EscapeTsv(GetColumnText(c.Column, c.Item)))));
        }

        return SetClipboardText(sb.ToString().TrimEnd('\r', '\n'));
    }

    /// <summary>
    /// Copies selected rows as tab-separated text (Excel-friendly). Returns false if nothing to copy.
    /// </summary>
    public static bool CopySelectedRows(DataGrid grid, bool includeHeaders = true)
    {
        var rows = grid.SelectedItems.Cast<object>()
            .Where(i => i is not null && !ReferenceEquals(i, CollectionView.NewItemPlaceholder))
            .ToList();

        // If user selected cells only, take distinct row items from those cells.
        if (rows.Count == 0 && grid.SelectedCells.Count > 0)
        {
            rows = grid.SelectedCells
                .Select(c => c.Item)
                .Where(i => i is not null && !ReferenceEquals(i, CollectionView.NewItemPlaceholder))
                .Distinct()
                .ToList()!;
        }

        if (rows.Count == 0 && grid.CurrentItem is not null &&
            !ReferenceEquals(grid.CurrentItem, CollectionView.NewItemPlaceholder))
        {
            rows.Add(grid.CurrentItem);
        }

        if (rows.Count == 0)
            return false;

        // Keep row order as in the grid.
        rows = rows.OrderBy(r => grid.Items.IndexOf(r)).ToList();

        var columns = grid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Where(c => c.Visibility == Visibility.Visible)
            .ToList();

        if (columns.Count == 0)
            return false;

        var sb = new StringBuilder();
        if (includeHeaders)
        {
            sb.AppendLine(string.Join('\t', columns.Select(c => EscapeTsv(c.Header?.ToString() ?? ""))));
        }

        foreach (var row in rows)
        {
            var cells = columns.Select(c => EscapeTsv(GetColumnText(c, row)));
            sb.AppendLine(string.Join('\t', cells));
        }

        return SetClipboardText(sb.ToString().TrimEnd('\r', '\n'));
    }

    private static bool SetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetColumnText(DataGridColumn column, object item)
    {
        if (column is DataGridBoundColumn bound && bound.Binding is Binding binding)
        {
            var path = binding.Path?.Path;
            if (!string.IsNullOrEmpty(path))
                return GetPropertyText(item, path);
        }

        // Checkbox column — copy Selected when present.
        var selectedProp = item.GetType().GetProperty("Selected");
        if (selectedProp?.PropertyType == typeof(bool) &&
            (column.Header?.ToString() == "✓" || column is DataGridTemplateColumn))
        {
            return selectedProp.GetValue(item) is true ? "✓" : "";
        }

        return "";
    }

    private static string EscapeTsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var text = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return text;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is DataGrid { IsVisible: true } grid)
            ScheduleSizeLastColumn(grid);
    }

    private static void HookItems(DataGrid grid)
    {
        UnhookItems(grid);
        if (grid.Items is not INotifyCollectionChanged incc)
            return;

        NotifyCollectionChangedEventHandler handler = (_, _) => ScheduleSizeLastColumn(grid);
        ItemHandlers.AddOrUpdate(grid, handler);
        incc.CollectionChanged += handler;
    }

    private static void UnhookItems(DataGrid grid)
    {
        if (!ItemHandlers.TryGetValue(grid, out var handler))
            return;

        if (grid.Items is INotifyCollectionChanged incc)
            incc.CollectionChanged -= handler;

        ItemHandlers.Remove(grid);
    }

    private static void ScheduleSizeLastColumn(DataGrid grid)
    {
        grid.Dispatcher.BeginInvoke(() => SizeLastColumnToContent(grid), DispatcherPriority.Loaded);
    }

    public static void SizeLastColumnToContent(DataGrid grid)
    {
        if (grid.Columns.Count == 0)
            return;

        grid.ClearValue(FrameworkElement.WidthProperty);
        grid.HorizontalAlignment = HorizontalAlignment.Stretch;

        for (var i = 0; i < grid.Columns.Count - 1; i++)
        {
            var col = grid.Columns[i];
            if (col.Width.IsAbsolute && col.Width.Value > 0)
                continue;
            col.Width = new DataGridLength(col.MinWidth > 0 ? col.MinWidth : 100);
        }

        var last = grid.Columns[^1];
        if (last is not DataGridBoundColumn bound || bound.Binding is not Binding binding)
            return;

        var path = binding.Path?.Path;
        if (string.IsNullOrEmpty(path))
            return;

        var min = last.MinWidth > 0 ? last.MinWidth : 120;
        var wrapCap = MaxColumnWidth;
        if (grid.ActualWidth > 1)
            wrapCap = Math.Clamp(grid.ActualWidth * 0.55, 420, MaxColumnWidth);

        var maxWidth = Math.Max(min, MeasureText(grid, last.Header?.ToString() ?? "") + CellPadding);

        foreach (var item in grid.Items)
        {
            if (item is null || ReferenceEquals(item, CollectionView.NewItemPlaceholder))
                continue;

            var text = GetPropertyText(item, path);
            if (string.IsNullOrEmpty(text))
                continue;

            maxWidth = Math.Max(maxWidth, MeasureText(grid, text) + CellPadding);
            if (maxWidth >= wrapCap)
            {
                maxWidth = wrapCap;
                break;
            }
        }

        last.Width = new DataGridLength(Math.Min(maxWidth, wrapCap));
    }

    private static string GetPropertyText(object item, string path)
    {
        object? current = item;
        foreach (var part in path.Split('.'))
        {
            if (current is null)
                return "";

            var prop = current.GetType().GetProperty(part);
            if (prop is null)
                return "";

            current = prop.GetValue(current);
        }

        return current?.ToString() ?? "";
    }

    private static double MeasureText(DataGrid grid, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var dpi = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
        var typeface = new Typeface(grid.FontFamily, grid.FontStyle, grid.FontWeight, grid.FontStretch);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            grid.FontSize,
            Brushes.Black,
            dpi);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid grid || Keyboard.Modifiers != ModifierKeys.Shift)
            return;

        var sv = FindScrollViewer(grid);
        if (sv is null)
            return;

        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;
            var nested = FindScrollViewer(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
