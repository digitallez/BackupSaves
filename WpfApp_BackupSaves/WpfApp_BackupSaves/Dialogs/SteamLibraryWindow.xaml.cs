using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using MessageBox = System.Windows.MessageBox;

namespace WpfApp_BackupSaves.Dialogs;

public partial class SteamLibraryWindow : Window
{
    public sealed class SteamRow
    {
        public required SteamGame Game { get; init; }
        public string Name => Game.Name;
        public ImageSource? Icon { get; init; }
    }

    private List<SteamRow> _all = [];
    private bool _loadStarted;
    private bool _loading;

    public string? SelectedCommand { get; private set; }

    public SteamLibraryWindow()
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
            _all = await Task.Run(() =>
            {
                var games = GameLaunchService.LoadSteamGames();
                return games.Select(g => new SteamRow
                {
                    Game = g,
                    Icon = GameLaunchService.GetLibraryIcon(g)
                }).ToList();
            }).ConfigureAwait(true);
            ApplyFilter();
        }
        finally
        {
            _loading = false;
            SetLoadingUi(false);
        }
    }

    private void SetLoadingUi(bool loading)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ContentPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = !loading;
        RefreshButton.IsEnabled = !loading;
        FilterBox.IsEnabled = !loading;
        if (!loading)
            EmptyText.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFilter()
    {
        var q = FilterBox.Text?.Trim() ?? "";
        IEnumerable<SteamRow> items = _all;
        if (!string.IsNullOrEmpty(q))
        {
            items = _all.Where(r =>
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Game.AppId.ToString().Contains(q, StringComparison.Ordinal));
        }

        GameList.ItemsSource = items.ToList();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && ContentPanel.Visibility == Visibility.Visible)
            ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

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

        if (GameList.SelectedItem is not SteamRow row)
        {
            MessageBox.Show(this, LocalizationService.Text("profile.steamSelectRequired"),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedCommand = row.Game.Command;
        DialogResult = true;
        Close();
    }
}
