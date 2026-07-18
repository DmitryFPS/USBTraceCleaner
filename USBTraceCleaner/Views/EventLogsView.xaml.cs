using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class EventLogsView : UserControl
{
    private readonly ObservableCollection<EventLogChannelRow> _channels = [];
    private readonly ICollectionView _view;
    private readonly StringBuilder _log = new();
    private readonly HashSet<string> _sidebarNamedGroups = new(StringComparer.OrdinalIgnoreCase);
    private string _activeGroup = WindowsEventLogBrowser.GroupAll;
    private int _selectedCount;

    public event Action<bool>? OperationBusyChanged;
    public event Action<string, IReadOnlyList<(string Group, string Label, int Count)>>? CategoriesChanged;

    public EventLogsView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_channels);
        _view.Filter = FilterChannel;
        ApplyGroupingAndSort();
        GridChannels.ItemsSource = _view;
        AppendLog($"Журналы событий {AppInfo.VersionLabel}");
        AppendLog("");
    }

    public string GetLogText() => _log.ToString();

    public int ChannelCount => _channels.Count;

    public string ActiveGroup => _activeGroup;

    public void SetCategoryFilter(string group)
    {
        _activeGroup = string.IsNullOrWhiteSpace(group) ? WindowsEventLogBrowser.GroupAll : group;
        ApplyGroupingAndSort();
        _view.Refresh();
        UpdateChannelCount();
        FitWindowsNameColumn();
    }

    public IReadOnlyList<(string Group, string Label, int Count)> GetCategoryCounts()
    {
        // Короткие категории слева: крупные группы по имени, мелкие — в «Прочие»
        var byGroup = _channels
            .GroupBy(c => c.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Count: g.Count()))
            .ToList();

        _sidebarNamedGroups.Clear();
        var result = new List<(string Group, string Label, int Count)>
        {
            (WindowsEventLogBrowser.GroupAll, WindowsEventLogBrowser.GroupAll, _channels.Count)
        };

        void AddNamed(string key)
        {
            var hit = byGroup.FirstOrDefault(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (hit.Count <= 0) return;
            _sidebarNamedGroups.Add(hit.Key);
            result.Add((hit.Key, hit.Key, hit.Count));
        }

        AddNamed(WindowsEventLogBrowser.GroupWindowsLogs);
        AddNamed(WindowsEventLogBrowser.GroupMicrosoft);

        var notable = byGroup
            .Where(g => !_sidebarNamedGroups.Contains(g.Key))
            .Where(g => g.Count >= 3)
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        foreach (var g in notable)
        {
            _sidebarNamedGroups.Add(g.Key);
            result.Add((g.Key, g.Key, g.Count));
        }

        var otherCount = byGroup
            .Where(g => !_sidebarNamedGroups.Contains(g.Key))
            .Sum(g => g.Count);
        if (otherCount > 0)
            result.Add((WindowsEventLogBrowser.GroupOther, WindowsEventLogBrowser.GroupOther, otherCount));

        return result;
    }

    public async Task RefreshChannelsAsync()
    {
        SetBusy(true);
        try
        {
            TxtPhase.Text = "Чтение всех журналов Event Viewer на этом ПК…";
            ProgressBar.IsIndeterminate = true;
            AppendLog("--- Обновление списка каналов (динамически с этой машины) ---");

            var found = await Task.Run(WindowsEventLogBrowser.ListChannels);

            foreach (var old in _channels)
                old.PropertyChanged -= Channel_PropertyChanged;

            // Массовая подмена без лагов: отвязать грид на время заполнения
            GridChannels.ItemsSource = null;
            _channels.Clear();
            foreach (var row in found)
            {
                row.PropertyChanged += Channel_PropertyChanged;
                _channels.Add(row);
            }

            _selectedCount = _channels.Count(c => c.Selected);

            AppendLog($"Доступных каналов на этом ПК: {found.Count}");
            foreach (var (group, _, count) in GetCategoryCounts().Skip(1).Take(12))
                AppendLog($"  {group}: {count}");

            if (!_channels.Any(c => c.Group.Equals(_activeGroup, StringComparison.OrdinalIgnoreCase))
                && _activeGroup != WindowsEventLogBrowser.GroupAll)
                _activeGroup = WindowsEventLogBrowser.GroupAll;

            ApplyGroupingAndSort();
            GridChannels.ItemsSource = _view;
            UpdateChannelCount();
            RaiseCategoriesChanged();
            FitWindowsNameColumn();
            TxtPhase.Text = $"Готово — {found.Count} журналов";
        }
        catch (Exception ex)
        {
            AppendLog($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "Журналы событий", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
        }
    }

    public void SelectAll()
    {
        foreach (var c in VisibleChannels())
        {
            if (c.IsSecurity && ChkIncludeSecurity.IsChecked != true)
                continue;
            c.Selected = true;
        }

        RecountSelected();
        UpdateChannelCount();
    }

    public void DeselectAll()
    {
        foreach (var c in VisibleChannels())
            c.Selected = false;

        RecountSelected();
        UpdateChannelCount();
    }

    public void ShowSelectedEvents(Window owner)
    {
        var row = GridChannels.SelectedItem as EventLogChannelRow
                  ?? VisibleChannels().FirstOrDefault(c => c.Selected)
                  ?? VisibleChannels().FirstOrDefault();

        if (row == null)
        {
            MessageBox.Show(owner, "Сначала обновите список каналов.", "Журналы событий",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EventLogEntriesWindow.ShowDialog(owner, row.Channel, row.DisplayName);
    }

    public async Task ClearSelectedAsync(Window owner)
    {
        if (!AdminHelper.IsAdministrator())
        {
            MessageBox.Show("Запустите программу от имени администратора.", "Нужны права",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var includeSecurity = ChkIncludeSecurity.IsChecked == true;
        var clearSystemLast = ChkClearSystemLast.IsChecked == true;
        // System — всегда последним среди выбранных (сначала остальные, потом Система)
        var selected = _channels
            .Where(c => c.Selected && (includeSecurity || !c.IsSecurity))
            .OrderBy(c => c.IsSystem ? 1 : 0)
            .ThenBy(c => c.Channel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(
                includeSecurity
                    ? "Отметьте каналы для очистки."
                    : "Отметьте каналы для очистки. Журнал «Безопасность» выключен галочкой.",
                "Журналы событий",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preview = selected.Take(25).Select(c => "• " + c.Channel);
        var names = string.Join("\n", preview);
        if (selected.Count > 25)
            names += $"\n… и ещё {selected.Count - 25}";

        var systemNote = clearSystemLast
            ? "\n\nВ конце «Система» будет полностью обнулена (wevtutil + файл System.evtx), чтобы не осталось Event ID 104."
            : "\n\nБез финальной очистки в «Система» останутся Event ID 104.";

        var confirm = MessageBox.Show(
            owner,
            $"Полностью очистить {selected.Count} журналов на этом ПК?\n\n{names}{systemNote}",
            "Подтверждение очистки журналов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        var ok = 0;
        var skip = 0;
        var denied = 0;
        var fail = 0;
        var deniedDetails = new List<string>();
        var failDetails = new List<string>();
        try
        {
            var totalSteps = selected.Count + (clearSystemLast ? 1 : 0);
            TxtPhase.Text = $"Очистка журналов… 0 / {totalSteps}";
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Maximum = totalSteps;
            ProgressBar.Value = 0;
            AppendLog($"--- Очистка {selected.Count} каналов ---");
            if (clearSystemLast)
                AppendLog("После списка — повторная очистка System (Event ID 104).");

            var progress = new Progress<(int Done, string Line)>(p =>
            {
                AppendLog(p.Line);
                ProgressBar.Value = p.Done;
                TxtPhase.Text = $"Очистка журналов… {p.Done} / {totalSteps}";
            });

            var result = await Task.Run(() =>
            {
                var localOk = 0;
                var localSkip = 0;
                var localDeniedCount = 0;
                var localFail = 0;
                var localDeniedList = new List<string>();
                var localFails = new List<string>();
                var done = 0;
                var reporter = (IProgress<(int Done, string Line)>)progress;

                void ApplyOutcome(string channel, EventLogClearOutcome outcome, string okPrefix = "[OK]")
                {
                    string line;
                    if (outcome.Ok)
                    {
                        localOk++;
                        line = $"{okPrefix}  {channel}";
                    }
                    else if (outcome.WasSkipped)
                    {
                        localSkip++;
                        line = $"[SKIP] {channel}: {outcome.Error}";
                    }
                    else if (outcome.WasAccessDenied)
                    {
                        localDeniedCount++;
                        line = $"[DENY] {channel}: закрыт политикой Windows";
                        localDeniedList.Add("• " + channel);
                    }
                    else
                    {
                        localFail++;
                        line = $"[FAIL] {channel}: {outcome.Error}";
                        localFails.Add($"• {channel}: {outcome.Error}");
                    }

                    done++;
                    if (done == 1 || done % 10 == 0 || done == totalSteps || !outcome.Ok)
                        reporter.Report((done, line));
                }

                foreach (var ch in selected)
                    ApplyOutcome(ch.Channel, WindowsEventLogBrowser.ClearChannel(ch.Channel));

                // Финал: убрать и 104 от других логов, и 104 от самой очистки System
                if (clearSystemLast)
                {
                    reporter.Report((done, "--- Полная очистка System (без остаточного Event ID 104) ---"));
                    var purgeLines = new List<string>();
                    var purge = WindowsEventLogBrowser.PurgeSystemLogCompletely(purgeLines.Add);
                    foreach (var pl in purgeLines)
                        reporter.Report((done, "  " + pl));
                    ApplyOutcome("System (полная)", purge, "[OK] финал");
                }

                return (localOk, localSkip, localDeniedCount, localFail, localDeniedList, localFails);
            });

            ok = result.localOk;
            skip = result.localSkip;
            denied = result.localDeniedCount;
            fail = result.localFail;
            deniedDetails = result.localDeniedList;
            failDetails = result.localFails;

            AppendLog($"Итог: OK={ok}, SKIP={skip}, DENY={denied}, FAIL={fail}");

            var summary = $"Очистка завершена.\nУспешно: {ok}\nПропущено: {skip}";
            if (denied > 0)
                summary += $"\nЗакрыто политикой Windows: {denied}";
            if (fail > 0)
                summary += $"\nОшибок: {fail}";

            if (clearSystemLast)
                summary += "\n\nЖурнал «Система» полностью обнулён в конце (без остаточного Event ID 104).";

            if (deniedDetails.Count > 0)
            {
                summary += "\n\nЗакрыты политикой Windows (это не ошибка программы):\n"
                           + string.Join("\n", deniedDetails.Take(8));
                if (deniedDetails.Count > 8)
                    summary += $"\n… и ещё {deniedDetails.Count - 8}";
                summary += "\n\nДаже при запуске от имени администратора к этим журналам "
                           + "доступ на очистку недоступен — так защищает сама ОС "
                           + "(например Microsoft Account / LiveId).";
            }

            if (failDetails.Count > 0)
            {
                summary += "\n\nРеальные ошибки:\n" + string.Join("\n", failDetails.Take(8));
                if (failDetails.Count > 8)
                    summary += $"\n… и ещё {failDetails.Count - 8}";
            }

            if (deniedDetails.Count > 0 || failDetails.Count > 0)
                summary += "\n\nПолный список — кнопка «Журнал».";

            MessageBox.Show(
                owner,
                summary,
                "Журналы событий",
                MessageBoxButton.OK,
                fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            TxtPhase.Text = "";
        }

        await RefreshChannelsAsync();
    }

    private void Channel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventLogChannelRow.Selected))
            RecountSelected();
    }

    private void RecountSelected() => _selectedCount = _channels.Count(c => c.Selected);

    private bool FilterChannel(object obj)
    {
        if (obj is not EventLogChannelRow row) return false;
        if (_activeGroup == WindowsEventLogBrowser.GroupAll) return true;
        if (_activeGroup == WindowsEventLogBrowser.GroupOther)
            return !_sidebarNamedGroups.Contains(row.Group);
        return row.Group.Equals(_activeGroup, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyGroupingAndSort()
    {
        using (_view.DeferRefresh())
        {
            _view.GroupDescriptions.Clear();
            _view.SortDescriptions.Clear();

            // Всегда: Группа → семейство канала (как дерево Event Viewer)
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EventLogChannelRow.Group)));
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EventLogChannelRow.ChannelFamily)));

            _view.SortDescriptions.Add(new SortDescription(nameof(EventLogChannelRow.GroupSortKey), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(EventLogChannelRow.Group), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(EventLogChannelRow.ChannelFamily), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(EventLogChannelRow.DisplayName), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(EventLogChannelRow.Channel), ListSortDirection.Ascending));
        }
    }

    /// <summary>Ширина «Имя в Windows» = самая длинная строка Channel (+ запас).</summary>
    private void FitWindowsNameColumn()
    {
        if (ColWindowsName == null || _channels.Count == 0)
            return;

        const double padding = 28;
        const double min = 180;
        const double max = 900;

        var dpi = VisualTreeHelper.GetDpi(GridChannels).PixelsPerDip;
        var typeface = new Typeface(
            GridChannels.FontFamily,
            GridChannels.FontStyle,
            GridChannels.FontWeight,
            GridChannels.FontStretch);

        double Measure(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                GridChannels.FontSize,
                Brushes.Black,
                dpi).WidthIncludingTrailingWhitespace;
        }

        var width = Measure(ColWindowsName.Header?.ToString() ?? "Имя в Windows") + padding;
        foreach (var row in _channels)
        {
            width = Math.Max(width, Measure(row.Channel) + padding);
            if (width >= max)
            {
                width = max;
                break;
            }
        }

        ColWindowsName.Width = new DataGridLength(Math.Clamp(width, min, max));
    }

    private IEnumerable<EventLogChannelRow> VisibleChannels() =>
        _channels.Where(FilterChannel);

    private void RaiseCategoriesChanged() =>
        CategoriesChanged?.Invoke(_activeGroup, GetCategoryCounts());

    private void GridChannels_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridChannels.SelectedItem is not EventLogChannelRow row)
            return;
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        EventLogEntriesWindow.ShowDialog(owner, row.Channel, row.DisplayName);
    }

    private void ChannelCheck_Click(object sender, RoutedEventArgs e) => UpdateChannelCount();

    private void UpdateChannelCount()
    {
        if (_activeGroup == WindowsEventLogBrowser.GroupAll)
        {
            TxtChannelCount.Text = $"Найдено: {_channels.Count} | Выбрано: {_selectedCount}";
            return;
        }

        var visible = 0;
        var selectedVisible = 0;
        foreach (var c in VisibleChannels())
        {
            visible++;
            if (c.Selected) selectedVisible++;
        }

        TxtChannelCount.Text =
            $"Группа «{_activeGroup}»: {visible} | Выбрано в группе: {selectedVisible} | Всего отмечено: {_selectedCount}";
    }

    private void SetBusy(bool busy) => OperationBusyChanged?.Invoke(busy);

    private void AppendLog(string line) => _log.AppendLine(line);
}
