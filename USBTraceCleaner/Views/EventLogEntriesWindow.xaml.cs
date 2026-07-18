using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using USBTraceCleaner.Controls;
using USBTraceCleaner.Services;

namespace USBTraceCleaner.Views;

[ExcludeFromCodeCoverage]
public partial class EventLogEntriesWindow : Window
{
    private readonly ObservableCollection<EventLogEntryRow> _events = [];
    private readonly string _channel;
    private bool _loading;

    public EventLogEntriesWindow(string channel, string? displayName = null)
    {
        InitializeComponent();
        _channel = channel;
        Title = $"События — {displayName ?? channel}";
        TxtTitle.Text = displayName ?? channel;
        TxtSubtitle.Text = channel;
        GridEvents.ItemsSource = _events;
        Loaded += async (_, _) => await LoadAsync();
    }

    public static void ShowDialog(Window owner, string channel, string? displayName = null)
    {
        var window = new EventLogEntriesWindow(channel, displayName) { Owner = owner };
        window.ShowDialog();
    }

    private async void BtnReload_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            await LoadAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        BtnReload.IsEnabled = false;
        ChkUsbOnly.IsEnabled = false;
        TxtCount.Text = "Загрузка…";

        var usbOnly = ChkUsbOnly.IsChecked == true;
        try
        {
            var events = await Task.Run(() =>
                WindowsEventLogBrowser.ReadEvents(_channel, WindowsEventLogBrowser.DefaultEventLimit, usbOnly));

            _events.Clear();
            foreach (var ev in events)
                _events.Add(ev);

            TxtCount.Text = usbOnly
                ? $"Записей: {events.Count} (USB-фильтр, до {WindowsEventLogBrowser.DefaultEventLimit})"
                : $"Записей: {events.Count} (последние до {WindowsEventLogBrowser.DefaultEventLimit})";
            DataGridScrollHelper.SizeLastColumnToContent(GridEvents);
        }
        catch (Exception ex)
        {
            TxtCount.Text = "Ошибка чтения";
            MessageBox.Show(this, ex.Message, "События журнала", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
            BtnReload.IsEnabled = true;
            ChkUsbOnly.IsEnabled = true;
        }
    }
}
