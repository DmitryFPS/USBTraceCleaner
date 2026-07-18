using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using USBTraceCleaner.Controls;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.Report;
using USBTraceCleaner.Views;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner;

[ExcludeFromCodeCoverage]
public partial class MainWindow : Window
{
    private readonly ObservableCollection<ArtifactItem> _allItems = [];
    private readonly ObservableCollection<CategoryFilterItem> _categories = [];
    private readonly ICollectionView _filteredView;
    private readonly ArtifactScanner _scanner = new();
    private readonly ArtifactCleaner _cleaner = new();
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private bool _isReady;
    private bool _operationInProgress;
    private bool _isNetworkTab;
    private bool _isOtherUsbTab;
    private ArtifactViewGroup _activeGroup = ArtifactViewGroup.All;
    private NetworkAuditFilterGroup _activeNetworkGroup = NetworkAuditFilterGroup.All;
    private ReportOperationType _lastUsbOperation = ReportOperationType.Scan;
    private CleanupResult? _lastUsbCleanResult;
    private CleanupOptions? _lastUsbOptions;
    private int _lastGhostRemoved;
    private int _lastGhostFailed;

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

        NetworkView.OperationBusyChanged += NetworkSetBusy;
        OtherUsbView.OperationBusyChanged += OtherUsbSetBusy;
        OtherUsbView.CountChanged += _ => RefreshOtherUsbSidebar();

        TxtAppVersion.Text = $"версия {AppInfo.Version}";

        AppendLog($"Очистка следов USB {AppInfo.VersionLabel} — Windows 10/11");
        AppendLog("Вкладка «Другие USB-следы» — хабы, камеры, DeviceMigration (не флешки).");
        AppendLog("Перед очисткой отключите все USB-накопители.");
        AppendLog("Слева — категории; «Призраки / дубликаты» — лишние записи PnP.");
        AppendLog("«Сканировать» — показать следы. «Очистить» — удалить выбранное.");
        AppendLog("«Удалить призраки» — быстро найти и удалить дубликаты PnP.");
        AppendLog("");
    }

    private void InitCategories() => RebuildUsbCategories(preserveSelection: false);

    private void RebuildUsbCategories(bool preserveSelection = true)
    {
        var prevGroup = _activeGroup;
        var wasReady = _isReady;
        _isReady = false;

        _categories.Clear();
        foreach (var group in ArtifactClassifier.OrderedGroups)
        {
            _categories.Add(new CategoryFilterItem
            {
                Group = group,
                Name = ArtifactClassifier.GetGroupLabel(group),
                Count = CountInGroup(group)
            });
        }

        if (preserveSelection)
        {
            var idx = _categories.ToList().FindIndex(c => c.Group == prevGroup);
            LstCategories.SelectedIndex = idx >= 0 ? idx : 0;
        }

        _isReady = wasReady;
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
            _lastUsbOptions = options;

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
                _lastUsbOperation = ReportOperationType.Clean;
                var result = await _cleaner.ExecuteAsync(_allItems, options, progress, _cts.Token);
                _lastUsbCleanResult = result;
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
                _lastUsbOperation = ReportOperationType.Scan;
                _lastUsbCleanResult = null;
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
            CleanExecutionArtifacts = ChkExecution.IsChecked == true,
            CleanExplorerMru = ChkExplorerMru.IsChecked == true,
            CleanRecycleBinUsb = ChkRecycle.IsChecked == true,
            CleanVolumeShadowCopies = ChkVss.IsChecked == true,
            CleanSelfTraces = ChkSelfTraces.IsChecked == true,
            CleanSystemEventLog = ChkSystemLog.IsChecked == true,
            CleanOrphanUsbFlags = true,
            CleanAllUsbFlags = true,
            FilterUserAssist = true,
            TryOfflineHiveClean = true,
            CleanEventLogs = true,
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
        if (!_isReady || _operationInProgress || LstCategories.SelectedItem is not CategoryFilterItem selected)
            return;

        if (_isNetworkTab)
        {
            _activeNetworkGroup = selected.NetworkGroup;
            NetworkView.SetCategoryFilter(_activeNetworkGroup);
            return;
        }

        if (_isOtherUsbTab)
            return;

        _activeGroup = selected.Group;
        _filteredView.Refresh();
        UpdateCount();
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _operationInProgress) return;
        _isNetworkTab = MainTabs.SelectedIndex == 1;
        _isOtherUsbTab = MainTabs.SelectedIndex == 2;
        UsbBottomBar.Visibility = _isNetworkTab || _isOtherUsbTab ? Visibility.Collapsed : Visibility.Visible;
        NetworkBottomBar.Visibility = _isNetworkTab ? Visibility.Visible : Visibility.Collapsed;
        OtherUsbBottomBar.Visibility = _isOtherUsbTab ? Visibility.Visible : Visibility.Collapsed;
        TxtSubtitle.Text = _isNetworkTab
            ? "Аудит и очистка сетевых следов"
            : _isOtherUsbTab
                ? "USB не-накопители: как «Другие следы» в USBDetector"
                : "Профессиональная очистка следов USB";
        RefreshSidebarCategories();
    }

    private void NetworkView_CategoriesChanged(
        NetworkAuditFilterGroup active,
        IReadOnlyList<(NetworkAuditFilterGroup Group, string Label, int Count)> counts)
    {
        if (!_isNetworkTab) return;

        var prevGroup = _activeNetworkGroup;
        var wasReady = _isReady;
        _isReady = false;

        _categories.Clear();
        foreach (var (group, label, count) in counts)
        {
            _categories.Add(new CategoryFilterItem
            {
                NetworkGroup = group,
                Name = label,
                Count = count
            });
        }

        var idx = _categories.ToList().FindIndex(c => c.NetworkGroup == prevGroup);
        LstCategories.SelectedIndex = idx >= 0 ? idx : 0;
        _activeNetworkGroup = prevGroup;

        _isReady = wasReady;
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
                    Name = label,
                    Count = count
                });
            }
            if (_categories.Count > 0)
                LstCategories.SelectedIndex = 0;
        }
        else if (_isOtherUsbTab)
        {
            _categories.Add(new CategoryFilterItem
            {
                Name = "Все",
                Count = OtherUsbView.ItemCount
            });
            LstCategories.SelectedIndex = 0;
        }
        else
        {
            RebuildUsbCategories();
            if (_categories.Count > 0 && LstCategories.SelectedIndex < 0)
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

    private void UpdateCategoryCounts() => RebuildUsbCategories();

    private int CountInGroup(ArtifactViewGroup group) =>
        group == ArtifactViewGroup.All
            ? _allItems.Count
            : _allItems.Count(i => i.ViewGroup == group);

    private void ArtifactCheck_Click(object sender, RoutedEventArgs e) => UpdateCount();

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

        DataGridScrollHelper.SizeLastColumnToContent(GridArtifacts);
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
            _lastUsbOperation = ReportOperationType.GhostClean;
            _lastGhostRemoved = result.Removed;
            _lastGhostFailed = result.Failed;
            _lastUsbCleanResult = new CleanupResult
            {
                ItemsProcessed = result.Removed,
                FailedCount = result.Failed,
                Success = result.Failed == 0
            };
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
    }

    private void NetworkHelp_Click(object sender, RoutedEventArgs e) => NetworkView.ShowHelp();

    private void NetworkSelectAll_Click(object sender, RoutedEventArgs e) => NetworkView.SelectAll();

    private void NetworkDeselect_Click(object sender, RoutedEventArgs e) => NetworkView.DeselectAll();

    private void BtnUsbLog_Click(object sender, RoutedEventArgs e) =>
        LogWindow.ShowDialog(this, "Журнал — USB", _log.ToString());

    private void BtnUsbReport_Click(object sender, RoutedEventArgs e)
    {
        if (_allItems.Count == 0 && _log.Length == 0)
        {
            MessageBox.Show("Сначала выполните сканирование или очистку.", "Отчёт PDF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var request = ReportExportService.BuildUsbReport(
            _lastUsbOperation,
            _allItems,
            _log.ToString(),
            _lastUsbOptions,
            _lastUsbCleanResult);

        ReportExportService.TrySavePdf(request, this);
    }

    private void RefreshOtherUsbSidebar()
    {
        if (!_isOtherUsbTab) return;
        RefreshSidebarCategories();
    }

    private void BtnOtherUsbLog_Click(object sender, RoutedEventArgs e) =>
        LogWindow.ShowDialog(this, "Журнал — другие USB-следы", OtherUsbView.GetLogText());

    private async void OtherUsbScan_Click(object sender, RoutedEventArgs e) => await OtherUsbView.ScanAsync();

    private async void OtherUsbClean_Click(object sender, RoutedEventArgs e) => await OtherUsbView.CleanSelectedAsync(this);

    private void OtherUsbSelectAll_Click(object sender, RoutedEventArgs e) => OtherUsbView.SelectAll();

    private void OtherUsbDeselect_Click(object sender, RoutedEventArgs e) => OtherUsbView.DeselectAll();

    private void OtherUsbSetBusy(bool busy)
    {
        SetOperationChrome(busy);
        BtnOtherUsbScan.IsEnabled = !busy;
        BtnOtherUsbClean.IsEnabled = !busy;
        BtnOtherUsbSelect.IsEnabled = !busy;
        BtnOtherUsbDeselect.IsEnabled = !busy;
        BtnOtherUsbLog.IsEnabled = !busy;
    }

    private void BtnNetworkLog_Click(object sender, RoutedEventArgs e) =>
        LogWindow.ShowDialog(this, "Журнал — аудит сети", NetworkView.GetLogText());

    private void BtnNetworkSummary_Click(object sender, RoutedEventArgs e) =>
        NetworkView.ShowSummaryDialog(this);

    private void BtnNetworkReport_Click(object sender, RoutedEventArgs e) => NetworkView.ExportPdfReport(this);

    private async void NetworkScan_Click(object sender, RoutedEventArgs e) => await NetworkView.ScanAsync();

    private async void NetworkClean_Click(object sender, RoutedEventArgs e) => await NetworkView.CleanAsync();

    private void NetworkSetBusy(bool busy)
    {
        SetOperationChrome(busy);
        BtnNetworkHelp.IsEnabled = !busy;
        BtnNetworkSelect.IsEnabled = !busy;
        BtnNetworkDeselect.IsEnabled = !busy;
        BtnNetworkScan.IsEnabled = !busy;
        BtnNetworkClean.IsEnabled = !busy;
        BtnNetworkLog.IsEnabled = !busy;
        BtnNetworkReport.IsEnabled = !busy;
        BtnNetworkSummary.IsEnabled = !busy;
    }

    private void SetOperationChrome(bool busy)
    {
        _operationInProgress = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private void SetBusy(bool busy)
    {
        SetOperationChrome(busy);
        BtnScan.IsEnabled = !busy;
        BtnClean.IsEnabled = !busy;
        BtnFixDuplicates.IsEnabled = !busy;
        BtnSelectAll.IsEnabled = !busy;
        BtnDeselectAll.IsEnabled = !busy;
        BtnUsbLog.IsEnabled = !busy;
        BtnUsbReport.IsEnabled = !busy;
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
        _log.AppendLine(line);
    }

    private sealed class CategoryFilterItem
    {
        public ArtifactViewGroup Group { get; init; } = ArtifactViewGroup.All;
        public NetworkAuditFilterGroup NetworkGroup { get; init; } = NetworkAuditFilterGroup.All;
        public string Name { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}
