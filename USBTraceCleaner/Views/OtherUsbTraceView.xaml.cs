using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using USBTraceCleaner.Controls;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class OtherUsbTraceView : UserControl
{
    private readonly ObservableCollection<OtherUsbTraceItem> _items = [];
    private readonly OtherUsbTraceScanner _scanner = new();
    private readonly StringBuilder _log = new();

    public event Action<bool>? OperationBusyChanged;
    public event Action<int>? CountChanged;

    public OtherUsbTraceView()
    {
        InitializeComponent();
        GridOtherTraces.ItemsSource = _items;
        AppendLog($"Другие USB-следы {AppInfo.VersionLabel}");
        AppendLog("");
    }

    public string GetLogText() => _log.ToString();

    public int ItemCount => _items.Count;

    public async Task ScanAsync()
    {
        SetBusy(true);
        try
        {
            TxtOtherPhase.Text = "Сканирование реестра…";
            ProgressOther.IsIndeterminate = true;
            AppendLog("--- Сканирование других USB-следов ---");

            var found = await Task.Run(() => _scanner.Scan(AppendLog));
            _items.Clear();
            foreach (var item in found)
                _items.Add(item);

            AppendLog($"Найдено устройств: {found.Count}");
            UpdateCount();
            CountChanged?.Invoke(_items.Count);
            TxtOtherPhase.Text = "Сканирование завершено";
        }
        catch (Exception ex)
        {
            AppendLog($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            ProgressOther.IsIndeterminate = false;
            ProgressOther.Value = 0;
        }
    }

    public async Task CleanSelectedAsync(Window owner)
    {
        if (!AdminHelper.IsAdministrator())
        {
            MessageBox.Show("Запустите программу от имени администратора.", "Нужны права",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = _items.Count(i => i.Selected);
        if (selected == 0)
        {
            MessageBox.Show("Отметьте устройства для удаления.", "Другие USB-следы",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Будет удалено следов устройств: {selected}\n\n" +
            "• Это не USB-флешки — хабы, камеры, виртуальные USB и архив DeviceMigration\n" +
            "• USB-мышь/клавиатура могут кратковременно отключиться\n" +
            "• После очистки нужна перезагрузка Windows\n\n" +
            "Продолжить?",
            "Очистить выбранное",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            TxtOtherPhase.Text = "Удаление…";
            ProgressOther.IsIndeterminate = true;

            var result = await Task.Run(() => OtherUsbTraceCleaner.Execute(_items, simulation: false, AppendLog));
            AppendLog(result.Log);

            if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                MessageBox.Show(result.ErrorMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ScanAsync();

            var msg = $"Обработано ключей: {result.Processed}\nОшибок: {result.Failed}\n\nПерезагрузите Windows.";
            if (result.Failed > 0 && result.FailedPaths.Count > 0)
            {
                msg += "\n\nНе удалено (первые пути):\n";
                foreach (var p in result.FailedPaths.Take(8))
                    msg += $"• {p}\n";
                if (result.FailedPaths.Count > 8)
                    msg += $"… и ещё {result.FailedPaths.Count - 8}\n";
            }
            if (!string.IsNullOrWhiteSpace(result.Hint))
                msg += $"\n{result.Hint}";

            MessageBox.Show(
                msg,
                "Готово",
                MessageBoxButton.OK,
                result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false);
            ProgressOther.IsIndeterminate = false;
            ProgressOther.Value = 0;
        }
    }

    public void SelectAll()
    {
        foreach (var item in _items)
            item.Selected = true;
        GridOtherTraces.Items.Refresh();
        UpdateCount();
    }

    public void DeselectAll()
    {
        foreach (var item in _items)
            item.Selected = false;
        GridOtherTraces.Items.Refresh();
        UpdateCount();
    }

    private void TraceCheck_Click(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var selected = _items.Count(i => i.Selected);
        TxtOtherCount.Text = $"Найдено: {_items.Count} | Выбрано: {selected}";
        DataGridScrollHelper.SizeLastColumnToContent(GridOtherTraces);
    }

    private void AppendLog(string line) => _log.AppendLine(line);

    private void SetBusy(bool busy) => OperationBusyChanged?.Invoke(busy);
}
