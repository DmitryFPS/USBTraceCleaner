using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.NetworkAudit;

namespace USBTraceCleaner.Views;

public partial class NetworkAuditView : UserControl
{
    private readonly ObservableCollection<NetworkAuditItem> _items = [];
    private readonly ICollectionView _view;
    private readonly NetworkAuditScanner _scanner = new();
    private readonly NetworkAuditCleaner _cleaner = new();
    private NetworkAuditFilterGroup _activeFilter = NetworkAuditFilterGroup.All;
    public event Action<NetworkAuditFilterGroup, IReadOnlyList<(NetworkAuditFilterGroup Group, string Label, int Count)>>? CategoriesChanged;

    public NetworkAuditView()
    {
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        InitializeComponent();

        GridNetwork.ItemsSource = _view;
        DpFrom.SelectedDate = DateTime.Today.AddDays(-30);
        DpTo.SelectedDate = DateTime.Today;

        AppendLog("Аудит сети — Windows 10/11");
        AppendLog("Источники: Wi‑Fi, Ethernet, VPN, DNS, реестр, журналы, SRU, роутер (SNMP/HTTP/ARP).");
        AppendLog("Укажите период и при необходимости логин роутера.");
        AppendLog("");
    }

    public void SetCategoryFilter(NetworkAuditFilterGroup group)
    {
        _activeFilter = group;
        _view.Refresh();
        UpdateCount();
    }

    public IReadOnlyList<(NetworkAuditFilterGroup Group, string Label, int Count)> GetCategoryCounts()
    {
        var groups = new[]
        {
            NetworkAuditFilterGroup.All,
            NetworkAuditFilterGroup.WiFi,
            NetworkAuditFilterGroup.Ethernet,
            NetworkAuditFilterGroup.Vpn,
            NetworkAuditFilterGroup.Router,
            NetworkAuditFilterGroup.Dns,
            NetworkAuditFilterGroup.EventLogs,
            NetworkAuditFilterGroup.Registry,
            NetworkAuditFilterGroup.Cache,
            NetworkAuditFilterGroup.UsbBluetooth,
            NetworkAuditFilterGroup.Other
        };

        return groups.Select(g => (g, FormatLabel(g), CountInGroup(g))).ToList();
    }

    private static string FormatLabel(NetworkAuditFilterGroup g) => g switch
    {
        NetworkAuditFilterGroup.All => "Все",
        NetworkAuditFilterGroup.WiFi => "Wi‑Fi",
        NetworkAuditFilterGroup.Ethernet => "Ethernet",
        NetworkAuditFilterGroup.Vpn => "VPN",
        NetworkAuditFilterGroup.Router => "Роутер",
        NetworkAuditFilterGroup.Dns => "DNS",
        NetworkAuditFilterGroup.EventLogs => "Журналы",
        NetworkAuditFilterGroup.Registry => "Реестр",
        NetworkAuditFilterGroup.Cache => "Кэши",
        NetworkAuditFilterGroup.UsbBluetooth => "USB / BT",
        _ => "Прочее"
    };

    private bool FilterItem(object obj)
    {
        if (obj is not NetworkAuditItem item) return false;
        if (_activeFilter == NetworkAuditFilterGroup.All) return true;
        return item.FilterGroup == _activeFilter;
    }

    private async void BtnNetScan_Click(object sender, RoutedEventArgs e) => await RunScan();

    private async void BtnNetClean_Click(object sender, RoutedEventArgs e)
    {
        if (!AdminHelper.IsAdministrator())
        {
            MessageBox.Show("Запустите программу от имени администратора.", "Нужны права",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = _items.Count(i => i.Selected && i.CanClean);
        if (selected == 0)
        {
            MessageBox.Show("Нет выбранных элементов для очистки.", "Аудит сети",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Будут очищены сетевые следы: {selected} элементов.\n\n" +
            "• Wi‑Fi профили и пароли\n• DNS / NetBIOS кэш\n• Журналы событий\n• Записи реестра сети\n\nПродолжить?",
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            var options = BuildOptions();
            var result = await Task.Run(() => _cleaner.Execute(_items, options, AppendLog));
            AppendLog(result.Log);
            await RunScan();
            MessageBox.Show($"Обработано: {result.Processed}\nОшибок: {result.Failed}",
                "Готово", MessageBoxButton.OK,
                result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunScan()
    {
        SetBusy(true);
        try
        {
            var options = BuildOptions();
            AppendLog($"--- Сканирование {options.DateFrom:dd.MM.yyyy} — {options.DateTo:dd.MM.yyyy} ---");

            var progress = new Progress<NetworkAuditProgress>(p =>
            {
                TxtNetworkPhase.Text = p.Phase;
                if (p.ItemsFound > 0)
                    ProgressNetwork.IsIndeterminate = false;
            });

            ProgressNetwork.IsIndeterminate = true;
            var found = await Task.Run(() => _scanner.Scan(options, progress));
            LoadResults(found, options.ShowSecrets);
            AppendLog($"Найдено записей: {found.Count}");
            TxtNetworkPhase.Text = "Сканирование завершено";
        }
        catch (Exception ex)
        {
            AppendLog($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            ProgressNetwork.IsIndeterminate = false;
            ProgressNetwork.Value = 0;
        }
    }

    private void LoadResults(IReadOnlyList<NetworkAuditItem> found, bool showSecrets)
    {
        _items.Clear();
        foreach (var item in found)
        {
            item.MaskSecrets = !showSecrets;
            _items.Add(item);
        }
        _view.Refresh();
        UpdateCount();
        CategoriesChanged?.Invoke(_activeFilter, GetCategoryCounts());
    }

    private NetworkAuditOptions BuildOptions() =>
        new()
        {
            DateFrom = DpFrom.SelectedDate?.Date ?? DateTime.Today.AddDays(-30),
            DateTo = (DpTo.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddSeconds(-1),
            ShowSecrets = ChkShowSecrets.IsChecked == true,
            SimulationMode = ChkSimulate.IsChecked == true,
            RouterIp = string.IsNullOrWhiteSpace(TxtRouterIp.Text) ? null : TxtRouterIp.Text.Trim(),
            RouterLogin = string.IsNullOrWhiteSpace(TxtRouterLogin.Text) ? null : TxtRouterLogin.Text.Trim(),
            RouterPassword = TxtRouterPassword.Password,
            ScanWiFi = ChkWiFi.IsChecked == true,
            ScanEthernet = ChkEthernet.IsChecked == true,
            ScanVpn = ChkVpn.IsChecked == true,
            ScanRouter = ChkRouter.IsChecked == true,
            ScanDns = ChkDns.IsChecked == true,
            ScanEventLogs = ChkLogs.IsChecked == true,
            ScanRegistry = ChkRegistry.IsChecked == true,
            ScanCaches = ChkCache.IsChecked == true,
            ScanUsbBluetooth = ChkUsbBt.IsChecked == true
        };

    private void BtnNetSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (NetworkAuditItem item in _view)
            if (item.CanClean) item.Selected = true;
        GridNetwork.Items.Refresh();
        UpdateCount();
    }

    private void BtnNetDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.Selected = false;
        GridNetwork.Items.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var visible = _view.Cast<NetworkAuditItem>().ToList();
        TxtNetworkCount.Text =
            $"Показано: {visible.Count} из {_items.Count} | Выбрано: {_items.Count(i => i.Selected)} | Очищаемых: {_items.Count(i => i.Selected && i.CanClean)}";
    }

    private int CountInGroup(NetworkAuditFilterGroup group) =>
        group == NetworkAuditFilterGroup.All
            ? _items.Count
            : _items.Count(i => i.FilterGroup == group);

    private void AppendLog(string line)
    {
        TxtNetworkLog.AppendText(line + Environment.NewLine);
        TxtNetworkLog.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        BtnNetScan.IsEnabled = !busy;
        BtnNetClean.IsEnabled = !busy;
        BtnNetSelectAll.IsEnabled = !busy;
        BtnNetDeselectAll.IsEnabled = !busy;
    }
}
