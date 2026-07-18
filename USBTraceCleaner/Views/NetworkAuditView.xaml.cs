using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using USBTraceCleaner.Controls;
using USBTraceCleaner.Models;
using USBTraceCleaner.Services;
using USBTraceCleaner.Services.NetworkAudit;
using USBTraceCleaner.Services.Report;
using System.Diagnostics.CodeAnalysis;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class NetworkAuditView : UserControl
{
    private readonly ObservableCollection<NetworkAuditItem> _items = [];
    private readonly ICollectionView _view;
    private readonly NetworkAuditScanner _scanner = new();
    private readonly NetworkAuditCleaner _cleaner = new();
    private readonly StringBuilder _log = new();
    private NetworkAuditFilterGroup _activeFilter = NetworkAuditFilterGroup.All;
    private ReportOperationType _lastOperation = ReportOperationType.Scan;
    private NetworkAuditOptions? _lastOptions;
    private NetworkAuditCleaner.CleanResult? _lastCleanResult;
    private NetworkAuditReadableSummary? _lastSummary;

    public event Action<NetworkAuditFilterGroup, IReadOnlyList<(NetworkAuditFilterGroup Group, string Label, int Count)>>? CategoriesChanged;
    public event Action<bool>? OperationBusyChanged;

    public NetworkAuditView()
    {
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        InitializeComponent();

        GridNetwork.ItemsSource = _view;
        DpFrom.SelectedDate = DateTime.Today.AddDays(-90);
        DpTo.SelectedDate = DateTime.Today;

        ChkShowUnknownOnly.Checked += (_, _) => { _view.Refresh(); UpdateCount(); };
        ChkShowUnknownOnly.Unchecked += (_, _) => { _view.Refresh(); UpdateCount(); };

        AppendLog($"Аудит сети {AppInfo.VersionLabel} — Windows 10/11");
        AppendLog("«Разрешено» = ваше подключение; при очистке удаляется вместе с остальными.");
        AppendLog("Максимальная очистка: все следы на ПК, отключение сети, перезагрузка.");
        AppendLog("");
    }

    public void ExportPdfReport(Window owner)
    {
        if (_items.Count == 0 && _log.Length == 0)
        {
            MessageBox.Show("Сначала выполните сканирование или очистку.", "Отчёт PDF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var request = ReportExportService.BuildNetworkReport(
            _lastOperation,
            _items,
            _log.ToString(),
            _lastOptions,
            _lastCleanResult);

        ReportExportService.TrySavePdf(request, owner);
    }

    public string GetLogText() => _log.ToString();

    public void ShowSummaryDialog(Window owner)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show(
                "Сначала выполните сканирование.",
                "Сводка подключений",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var summary = NetworkAuditSummaryBuilder.Build(_items, BuildOptions().Whitelist);
        _lastSummary = summary;
        NetworkSummaryWindow.ShowDialog(owner, summary);
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

    public void ShowHelp()
    {
        var unknown = _items.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown);
        var cleanable = _items.Count(i => i.CanClean);
        MessageBox.Show(
            NetworkAuditHints.HelpText +
            $"\n\nСейчас: всего {_items.Count}, неизвестных {unknown}, для очистки {cleanable}.",
            "Справка по аудиту сети",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void SelectAll()
    {
        foreach (NetworkAuditItem item in _view)
            item.Selected = item.CanClean;
        GridNetwork.Items.Refresh();
        UpdateCount();
    }

    public void DeselectAll()
    {
        foreach (var item in _items)
            item.Selected = false;
        GridNetwork.Items.Refresh();
        UpdateCount();
    }

    public async Task ScanAsync() => await RunScan();

    public async Task CleanAsync()
    {
        if (!AdminHelper.IsAdministrator())
        {
            MessageBox.Show("Запустите программу от имени администратора.", "Нужны права",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = BuildOptions();
        var fullClean = options.FullCleanMode;

        if (fullClean)
        {
            foreach (var item in _items.Where(i => i.CanClean))
                item.Selected = true;
        }

        var selected = _items.Count(i => i.Selected && i.CanClean);
        if (selected == 0)
        {
            MessageBox.Show("Нет элементов для очистки.", "Аудит сети",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (fullClean && IsVpnAdapterActive())
        {
            var vpnWarn = MessageBox.Show(
                "Обнаружен активный VPN-адаптер (happ-tun или другой туннель).\n\n" +
                "Закройте HAPP/VPN перед очисткой — иначе следы VPN могут остаться.\n\n" +
                "Продолжить очистку?",
                "VPN активен",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (vpnWarn != MessageBoxResult.Yes) return;
        }

        if (_items.Any(i => i.Kind == NetworkAuditKind.HostsFile && i.CanClean))
        {
            var hostsAnswer = MessageBox.Show(
                NetworkAuditHints.HostsWarning,
                "Файл hosts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            options.CleanHostsFile = hostsAnswer == MessageBoxResult.Yes;
        }

        var answer = MessageBox.Show(
            NetworkAuditHints.BuildCleanupWarning(_items, fullClean),
            fullClean ? "Максимальная очистка" : "Подтверждение очистки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _cleaner.Execute(_items, options, AppendLog));
            _lastOperation = ReportOperationType.Clean;
            _lastCleanResult = result;
            _lastOptions = options;

            var failDetails = result.Failures.Count > 0
                ? "\n\nОшибки (требуют внимания):\n" + string.Join("\n", result.Failures)
                : string.Empty;
            var skipDetails = result.SkippedItems.Count > 0
                ? "\n\nПропущено (нет в Windows):\n" + string.Join("\n", result.SkippedItems)
                : string.Empty;
            var stats = $"Успешно: {result.Processed}, пропущено: {result.Skipped}, ошибок: {result.Failed}";

            if (options.RebootAfterClean)
            {
                MessageBox.Show(
                    "Очистка выполнена.\n\n" +
                    "Wi‑Fi сессия разорвана (адаптер не отключается).\n" +
                    "Windows перезагрузится через 60 секунд.\n\n" +
                    "После перезагрузки можно сразу сохранить «Отчёт PDF» без интернета.\n\n" +
                    stats + failDetails + skipDetails,
                    "Готово",
                    MessageBoxButton.OK,
                    result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            else
            {
                await RunScan();
                MessageBox.Show(
                    stats + failDetails + skipDetails,
                    "Готово", MessageBoxButton.OK,
                    result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }
        finally
        {
            SetBusy(false);
        }
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
        if (_activeFilter != NetworkAuditFilterGroup.All && item.FilterGroup != _activeFilter)
            return false;
        if (ChkShowUnknownOnly.IsChecked == true &&
            item.AuthorizationStatus != NetworkAuthorizationStatus.Unknown)
            return false;
        return true;
    }

    private async Task RunScan()
    {
        SetBusy(true);
        try
        {
            var options = BuildOptions();
            _lastOptions = options;
            _lastOperation = ReportOperationType.Scan;
            _lastCleanResult = null;
            AppendLog($"--- Сканирование {options.DateFrom:dd.MM.yyyy} — {options.DateTo:dd.MM.yyyy} ---");

            var progress = new Progress<NetworkAuditProgress>(p =>
            {
                TxtNetworkPhase.Text = p.Phase;
                if (p.ItemsFound > 0)
                    ProgressNetwork.IsIndeterminate = false;
            });

            ProgressNetwork.IsIndeterminate = true;
            var found = await Task.Run(() => _scanner.Scan(options, progress));
            LoadResults(found, options);
            _lastSummary = NetworkAuditSummaryBuilder.Build(found, options.Whitelist);
            AppendLog("");
            AppendLog("--- Сводка ---");
            AppendLog(_lastSummary.ToPlainText());
            AppendLog($"Найдено записей: {found.Count}");
            var unknown = found.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown);
            AppendLog($"Неизвестных (не в белом списке): {unknown}");
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

    private void LoadResults(IReadOnlyList<NetworkAuditItem> found, NetworkAuditOptions options)
    {
        _items.Clear();
        foreach (var item in found)
        {
            item.MaskSecrets = !options.ShowSecrets;
            item.Selected = options.FullCleanMode ? item.CanClean : item.CanClean;
            _items.Add(item);
        }
        _view.Refresh();
        UpdateCount();
        CategoriesChanged?.Invoke(_activeFilter, GetCategoryCounts());
    }

    private NetworkAuditOptions BuildOptions() =>
        new()
        {
            DateFrom = DpFrom.SelectedDate?.Date ?? DateTime.Today.AddDays(-90),
            DateTo = (DpTo.SelectedDate?.Date ?? DateTime.Today).AddDays(1).AddSeconds(-1),
            ShowSecrets = ChkShowSecrets.IsChecked == true,
            SimulationMode = ChkSimulation.IsChecked == true,
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
            ScanUsbBluetooth = ChkUsbBt.IsChecked == true,
            Whitelist = NetworkAuditWhitelist.Parse(TxtAllowedIps.Text, TxtAllowedWiFi.Text, TxtAllowedVpn.Text),
            FullCleanMode = ChkFullClean.IsChecked == true,
            DisconnectNetwork = ChkDisconnect.IsChecked == true,
            RebootAfterClean = ChkReboot.IsChecked == true,
            ShowUnknownOnly = ChkShowUnknownOnly.IsChecked == true
        };

    private void NetworkCheck_Click(object sender, RoutedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var visible = _view.Cast<NetworkAuditItem>().ToList();
        var cleanable = _items.Count(i => i.CanClean);
        var unknown = _items.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Unknown);
        var allowed = _items.Count(i => i.AuthorizationStatus == NetworkAuthorizationStatus.Allowed);
        var selectedClean = _items.Count(i => i.Selected && i.CanClean);
        TxtNetworkCount.Text =
            $"Показано: {visible.Count} из {_items.Count} | Разрешено: {allowed} | Неизвестно: {unknown} | " +
            $"Для очистки: {cleanable} | Отмечено: {selectedClean}";
        DataGridScrollHelper.SizeLastColumnToContent(GridNetwork);
    }

    private int CountInGroup(NetworkAuditFilterGroup group) =>
        group == NetworkAuditFilterGroup.All
            ? _items.Count
            : _items.Count(i => i.FilterGroup == group);

    private void AppendLog(string line) => _log.AppendLine(line);

    private static bool IsVpnAdapterActive()
    {
        try
        {
            var output = ProcessRunner.Run("powershell", "-NoProfile -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and ($_.InterfaceDescription -like '*tun*' -or $_.InterfaceDescription -like '*Tunnel*' -or $_.Name -like '*happ*') } | Select-Object -ExpandProperty Name\"");
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private void SetBusy(bool busy) => OperationBusyChanged?.Invoke(busy);
}
