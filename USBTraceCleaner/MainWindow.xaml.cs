using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Views;

namespace USBTraceCleaner;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ArtifactItem> _allItems = [];
    private readonly ObservableCollection<CategoryFilterItem> _categories = [];
    private readonly ICollectionView _filteredView;
    private readonly ArtifactScanner _scanner = new();
    private readonly ArtifactCleaner _cleaner = new();
    private CancellationTokenSource? _cts;
    private bool _isReady;
    private bool _isNetworkTab;
    private ArtifactViewGroup _activeGroup = ArtifactViewGroup.All;
    private NetworkAuditFilterGroup _activeNetworkGroup = NetworkAuditFilterGroup.All;

    public MainWindow()
    {
        _filteredView = CollectionViewSource.GetDefaultView(_allItems);
        _filteredView.Filter = FilterItem;

        InitializeComponent();

        GridArtifacts.ItemsSource = _filteredView;
        LstCategories.ItemsSource = _categories;
        LstCategories.SelectionChanged += LstCategories_SelectionChanged;

        InitCategories();
        LstCategories.SelectedIndex = 0;
        _isReady = true;

        TxtOsBadge.Text = AdminHelper.GetWindowsVersionLabel();

        if (!AdminHelper.IsAdministrator())
            TxtAdminWarning.Visibility = Visibility.Visible;

        AppendLog("Очистка следов USB v1.1 — Windows 10/11");
        AppendLog("Перед очисткой отключите все USB-накопители.");
        AppendLog("Слева — категории; «Призраки / дубликаты» — лишние записи PnP.");
        AppendLog("«Сканировать» — показать следы. «Очистить» — удалить выбранное.");
        AppendLog("«Удалить призраки» — быстро найти и удалить дубликаты PnP.");
        AppendLog("");
    }

    private void InitCategories()
    {
        _categories.Clear();
        foreach (var group in ArtifactClassifier.OrderedGroups)
        {
            _categories.Add(new CategoryFilterItem
            {
                Group = group,
                Label = FormatCategoryLabel(group, 0)
            });
        }
    }

    private async void BtnScan_Click(object sender, RoutedEventArgs e) => await RunOperation(scanOnly: true);

    private async void BtnClean_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Будут УДАЛЕНЫ следы USB-устройств из реестра (реальная очистка).\n\n" +
            "• Отключите все USB-флешки и внешние диски\n" +
            "• Рядом с программой сохранится .reg-бэкап\n" +
            "• Ядро очистки всегда удаляет USB-накопители\n" +
            "• После очистки ОБЯЗАТЕЛЬНА перезагрузка\n\n" +
            "Продолжить?",
            "Подтверждение очистки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        await RunOperation(scanOnly: false);
    }

    private async Task RunOperation(bool scanOnly)
    {
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            var options = BuildOptions();

            AppendLog($"--- {(scanOnly ? "Сканирование" : "Очистка")} ---");

            var progress = new Progress<CleanupProgress>(p =>
            {
                TxtPhase.Text = p.Phase;
                if (p.ItemsFound > 0)
                    ProgressBar.Value = (double)p.ItemsProcessed / p.ItemsFound * 100;
            });

            if (scanOnly || _allItems.Count == 0)
            {
                var found = await Task.Run(() => _scanner.Scan(options, progress), _cts.Token);
                LoadScanResults(found);
            }

            if (!scanOnly)
            {
                var result = await _cleaner.ExecuteAsync(_allItems, options, progress, _cts.Token);
                AppendLog(result.Log);
                LoadScanResults(_allItems.ToList());
                UpdateCategoryCounts();

                if (!result.Success)
                {
                    MessageBox.Show(result.ErrorMessage ?? "Ошибка", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    var msg = $"Очистка завершена.\nОбработано: {result.ItemsProcessed} элементов.";
                    msg += $"\nСледов USB-накопителей осталось: {result.UsbStorRemaining}";
                    if (result.FailedCount > 0)
                        msg += $"\n\n⚠ Не удалось: {result.FailedCount} (см. лог)";
                    msg += options.RebootAfterClean
                        ? "\n\nWindows перезагрузится через 5 секунд."
                        : "\n\n⚠ Перезагрузите Windows — без этого USBDeview может показывать старые данные!";
                    MessageBox.Show(msg, "Готово", MessageBoxButton.OK,
                        result.FailedCount > 0 || result.UsbStorRemaining > 0
                            ? MessageBoxImage.Warning
                            : MessageBoxImage.Information);
                }
            }
            else
            {
                var storageTraces = CountInGroup(ArtifactViewGroup.UsbStorage);
                AppendLog($"Сканирование завершено. Всего: {_allItems.Count}, USB-накопителей: {storageTraces}");
                TxtPhase.Text = "Сканирование завершено";
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Операция отменена.");
        }
        catch (Exception ex)
        {
            AppendLog($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            ProgressBar.Value = 0;
        }
    }

    private void LoadScanResults(IEnumerable<ArtifactItem> found)
    {
        _allItems.Clear();
        foreach (var item in found)
            _allItems.Add(item);
        UpdateCategoryCounts();
        _filteredView.Refresh();
        UpdateCount();
        ScheduleAutoFitGridColumns();
    }

    private void ScheduleAutoFitGridColumns()
    {
        Dispatcher.BeginInvoke(AutoFitGridColumns, DispatcherPriority.Loaded);
    }

    private void AutoFitGridColumns()
    {
        if (GridArtifacts.Columns.Count == 0)
            return;

        GridArtifacts.UpdateLayout();

        foreach (var column in GridArtifacts.Columns)
        {
            if (column is DataGridCheckBoxColumn)
            {
                column.Width = new DataGridLength(40);
                continue;
            }

            column.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells);
        }

        GridArtifacts.UpdateLayout();
    }

    private CleanupOptions BuildOptions()
    {
        return new CleanupOptions
        {
            SimulationMode = false,
            SaveBackup = ChkBackup.IsChecked == true,
            CreateRestorePoint = ChkRestorePoint.IsChecked == true,
            CloseExplorer = ChkCloseExplorer.IsChecked == true,
            RebootAfterClean = ChkReboot.IsChecked == true,
            CleanMtpDevices = ChkMtp.IsChecked == true,
            CleanAllUsbDevices = false,
            CleanShellBags = ChkShellBags.IsChecked == true,
            CleanRecentLinks = ChkRecentLinks.IsChecked == true,
            CleanBamEntries = ChkBam.IsChecked == true,
            ScrubLogFiles = ChkScrubLogs.IsChecked == true,
            PreserveLogFileTimestamps = ChkPreserveLogDates.IsChecked == true,
        };
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ArtifactItem item) return false;
        if (!_isReady) return true;

        if (_activeGroup == ArtifactViewGroup.All)
            return true;

        return item.ViewGroup == _activeGroup;
    }

    private void LstCategories_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_isReady || LstCategories.SelectedItem is not CategoryFilterItem selected)
            return;

        if (_isNetworkTab)
        {
            _activeNetworkGroup = selected.NetworkGroup;
            NetworkView.SetCategoryFilter(_activeNetworkGroup);
            return;
        }

        _activeGroup = selected.Group;
        _filteredView.Refresh();
        UpdateCount();
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        _isNetworkTab = MainTabs.SelectedIndex == 1;
        UsbBottomBar.Visibility = _isNetworkTab ? Visibility.Collapsed : Visibility.Visible;
        TxtSubtitle.Text = _isNetworkTab
            ? "Аудит и очистка сетевых следов"
            : "Профессиональная очистка следов USB";
        RefreshSidebarCategories();
    }

    private void NetworkView_CategoriesChanged(
        NetworkAuditFilterGroup active,
        IReadOnlyList<(NetworkAuditFilterGroup Group, string Label, int Count)> counts)
    {
        if (!_isNetworkTab) return;
        _categories.Clear();
        foreach (var (group, label, count) in counts)
        {
            _categories.Add(new CategoryFilterItem
            {
                NetworkGroup = group,
                Label = $"{label} ({count})"
            });
        }
        LstCategories.Items.Refresh();
    }

    private void RefreshSidebarCategories()
    {
        _isReady = false;
        _categories.Clear();
        if (_isNetworkTab)
        {
            foreach (var (group, label, count) in NetworkView.GetCategoryCounts())
            {
                _categories.Add(new CategoryFilterItem
                {
                    NetworkGroup = group,
                    Label = $"{label} ({count})"
                });
            }
            if (_categories.Count > 0)
                LstCategories.SelectedIndex = 0;
        }
        else
        {
            InitCategories();
            LstCategories.SelectedIndex = 0;
        }
        _isReady = true;
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _filteredView.Cast<ArtifactItem>())
            item.Selected = true;
        GridArtifacts.Items.Refresh();
        UpdateCount();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _filteredView.Cast<ArtifactItem>())
            item.Selected = false;
        GridArtifacts.Items.Refresh();
        UpdateCount();
    }

    private void UpdateCategoryCounts()
    {
        foreach (var cat in _categories)
            cat.Label = FormatCategoryLabel(cat.Group, CountInGroup(cat.Group));
        LstCategories.Items.Refresh();
    }

    private int CountInGroup(ArtifactViewGroup group) =>
        group == ArtifactViewGroup.All
            ? _allItems.Count
            : _allItems.Count(i => i.ViewGroup == group);

    private static string FormatCategoryLabel(ArtifactViewGroup group, int count) =>
        $"{ArtifactClassifier.GetGroupLabel(group)} ({count})";

    private void UpdateCount()
    {
        var visible = _filteredView.Cast<ArtifactItem>().ToList();
        var selectedVisible = visible.Count(i => i.Selected);
        var selectedAll = _allItems.Count(i => i.Selected);
        var storage = CountInGroup(ArtifactViewGroup.UsbStorage);

        if (_activeGroup == ArtifactViewGroup.All)
        {
            TxtFoundCount.Text =
                $"Показано: {visible.Count} из {_allItems.Count} | USB-накопителей: {storage} | Выбрано: {selectedAll}";
        }
        else
        {
            TxtFoundCount.Text =
                $"В разделе: {visible.Count} | Выбрано в разделе: {selectedVisible} | Всего выбрано: {selectedAll}";
        }
    }

    private async void BtnFixDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (!AdminHelper.IsAdministrator())
        {
            MessageBox.Show("Запустите программу от имени администратора.", "Нужны права",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            AppendLog("--- Поиск призраков и дубликатов PnP ---");
            var ghosts = await Task.Run(() => PnPGhostScanner.Scan());
            AppendLog($"Найдено: {ghosts.Count}");

            MergeGhostScanResults(ghosts);
            SelectCategory(ArtifactViewGroup.PnPGhosts);

            if (ghosts.Count == 0)
            {
                MessageBox.Show("Призраки и дубликаты PnP не найдены.", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                $"Найдено записей: {ghosts.Count}\n\n" +
                "• Дубликаты — лишние instance ID одного устройства\n" +
                "• Призраки — запись в реестре без активного устройства\n\n" +
                "Удалить все найденные записи?",
                "Призраки / дубликаты PnP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                AppendLog("Удаление отменено — записи показаны в таблице (категория «Призраки / дубликаты»).");
                return;
            }

            AppendLog("--- Удаление призраков и дубликатов ---");
            var result = await Task.Run(() => PnPGhostScanner.RemoveSelected(_allItems, AppendLog));
            MergeGhostScanResults(PnPGhostScanner.Scan());
            AppendLog($"Удалено: {result.Removed}, ошибок: {result.Failed}");

            MessageBox.Show(
                $"Удалено: {result.Removed}\nОшибок: {result.Failed}\n\nПерезагрузите Windows.",
                "Готово",
                MessageBoxButton.OK,
                result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void MergeGhostScanResults(IReadOnlyList<ArtifactItem> ghosts)
    {
        for (var i = _allItems.Count - 1; i >= 0; i--)
        {
            if (_allItems[i].Category == ArtifactCategory.PnPGhosts)
                _allItems.RemoveAt(i);
        }

        foreach (var ghost in ghosts)
            _allItems.Add(ghost);

        UpdateCategoryCounts();
        _filteredView.Refresh();
        UpdateCount();
        ScheduleAutoFitGridColumns();
    }

    private void SelectCategory(ArtifactViewGroup group)
    {
        _activeGroup = group;
        for (var i = 0; i < _categories.Count; i++)
        {
            if (_categories[i].Group != group) continue;
            LstCategories.SelectedIndex = i;
            break;
        }
        _filteredView.Refresh();
        UpdateCount();
    }

    private void AppendLog(string line)
    {
        TxtLog.AppendText(line + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        BtnScan.IsEnabled = !busy;
        BtnClean.IsEnabled = !busy;
        BtnFixDuplicates.IsEnabled = !busy;
        BtnSelectAll.IsEnabled = !busy;
        BtnDeselectAll.IsEnabled = !busy;
        LstCategories.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private sealed class CategoryFilterItem
    {
        public ArtifactViewGroup Group { get; init; } = ArtifactViewGroup.All;
        public NetworkAuditFilterGroup NetworkGroup { get; init; } = NetworkAuditFilterGroup.All;
        public string Label { get; set; } = string.Empty;
    }
}
