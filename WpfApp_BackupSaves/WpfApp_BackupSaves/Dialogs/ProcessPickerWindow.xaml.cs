using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using MessageBox = System.Windows.MessageBox;

namespace WpfApp_BackupSaves.Dialogs;

public partial class ProcessPickerWindow : Window
{
    public sealed class ProcessRow
    {
        public required string Value { get; init; }
        public required string Display { get; init; }
    }

    private List<ProcessRow> _all = [];
    private bool _loadStarted;
    private bool _loading;
    public string? SelectedPattern { get; private set; }

    public ProcessPickerWindow()
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        if (_loadStarted)
            return;
        _loadStarted = true;
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_loading)
            return;

        _loading = true;
        SetLoadingUi(true);
        try
        {
            var rows = await Task.Run(CollectProcessRows).ConfigureAwait(true);
            _all = rows;
            ApplyFilter();
        }
        finally
        {
            _loading = false;
            SetLoadingUi(false);
        }
    }

    private static List<ProcessRow> CollectProcessRows() =>
        ProcessWatchService.ListDistinctRunning()
            .Select(p => new ProcessRow
            {
                Value = string.IsNullOrWhiteSpace(p.Path) ? p.Name : p.Path,
                Display = string.IsNullOrWhiteSpace(p.Path)
                    ? $"{p.Name}  (pid {p.Pid})"
                    : $"{p.Name}  —  {p.Path}"
            })
            .ToList();

    private void SetLoadingUi(bool loading)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = !loading;
        RefreshButton.IsEnabled = !loading;
        FilterBox.IsEnabled = !loading;
    }

    private void ApplyFilter()
    {
        var q = FilterBox.Text?.Trim() ?? "";
        IEnumerable<ProcessRow> items = _all;
        if (!string.IsNullOrEmpty(q))
        {
            items = _all.Where(r =>
                r.Display.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Value.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        ProcessList.ItemsSource = items.ToList();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && ContentPanel.Visibility == Visibility.Visible)
            ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Accept()
    {
        if (_loading)
            return;

        if (ProcessList.SelectedItem is not ProcessRow row)
        {
            MessageBox.Show(this, LocalizationService.Text("process.selectRequired"),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedPattern = row.Value;
        DialogResult = true;
        Close();
    }
}
