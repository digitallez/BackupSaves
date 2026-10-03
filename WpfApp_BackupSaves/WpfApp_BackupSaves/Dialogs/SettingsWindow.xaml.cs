using System.Windows;
using System.Windows.Controls;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using WpfApp_BackupSaves.ViewModels;

namespace WpfApp_BackupSaves.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _app;
    private readonly ISettingsStore _settings;
    private readonly Action _openLogs;
    private readonly Func<Task> _checkUpdates;
    private readonly Action _onThemeChanged;
    private readonly Action<string> _onLanguageChanged;
    private bool _suppress;

    public SettingsWindow(
        AppSettings app,
        ISettingsStore settings,
        Action openLogs,
        Func<Task> checkUpdates,
        Action onThemeChanged,
        Action<string> onLanguageChanged)
    {
        _app = app;
        _settings = settings;
        _openLogs = openLogs;
        _checkUpdates = checkUpdates;
        _onThemeChanged = onThemeChanged;
        _onLanguageChanged = onLanguageChanged;

        InitializeComponent();
        CustomWindowChrome.Apply(this);
        LoadControls();
    }

    private void LoadControls()
    {
        _suppress = true;
        try
        {
            ThemeCombo.Items.Clear();
            ThemeCombo.Items.Add(new ThemeItem(AppTheme.Dark, LocalizationService.Text("settings.themeDark")));
            ThemeCombo.Items.Add(new ThemeItem(AppTheme.Light, LocalizationService.Text("settings.themeLight")));
            ThemeCombo.DisplayMemberPath = nameof(ThemeItem.Label);
            ThemeCombo.SelectedItem = ThemeCombo.Items.Cast<ThemeItem>()
                .FirstOrDefault(t => t.Theme == _app.Ui.Theme)
                ?? ThemeCombo.Items[0];

            LanguageCombo.Items.Clear();
            foreach (var info in LocalizationService.Instance.DiscoverLanguages())
            {
                LanguageCombo.Items.Add(new LanguageOption
                {
                    Id = info.Id,
                    Code = info.DisplayCode,
                    Name = info.DisplayName,
                    FlagUri = info.FlagUri
                });
            }

            var lang = LocalizationService.Instance.Language;
            LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<LanguageOption>()
                .FirstOrDefault(l => l.Id.Equals(lang, StringComparison.OrdinalIgnoreCase))
                ?? LanguageCombo.Items.Cast<LanguageOption>().FirstOrDefault();

            CloseActionCombo.Items.Clear();
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Ask, LocalizationService.Text("settings.closeAsk")));
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.HideToTray, LocalizationService.Text("settings.closeTray")));
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Exit, LocalizationService.Text("settings.closeExit")));
            CloseActionCombo.DisplayMemberPath = nameof(CloseItem.Label);
            CloseActionCombo.SelectedItem = CloseActionCombo.Items.Cast<CloseItem>()
                .FirstOrDefault(c => c.Value == _app.Ui.CloseAction)
                ?? CloseActionCombo.Items[0];

            RefreshInstallButton();
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RefreshInstallButton()
    {
        var installed = AppInstallService.IsRunningFromInstallDirectory();
        InstallButton.IsEnabled = !installed;
        InstallButtonText.Text = installed
            ? LocalizationService.Text("settings.installed")
            : LocalizationService.Text("settings.install");
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ThemeCombo.SelectedItem is not ThemeItem item)
            return;

        ThemeManager.Apply(item.Theme);
        _app.Ui.Theme = item.Theme;
        await SaveAsync();
        _onThemeChanged();
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || LanguageCombo.SelectedItem is not LanguageOption opt)
            return;

        LocalizationService.Instance.SetLanguage(opt.Id);
        _app.Ui.Language = opt.Id;
        await SaveAsync();
        _onLanguageChanged(opt.Id);
        // Refresh labels that are not Loc-bound
        _suppress = true;
        try
        {
            if (ThemeCombo.SelectedItem is ThemeItem curTheme)
            {
                var idx = ThemeCombo.SelectedIndex;
                ThemeCombo.Items.Clear();
                ThemeCombo.Items.Add(new ThemeItem(AppTheme.Dark, LocalizationService.Text("settings.themeDark")));
                ThemeCombo.Items.Add(new ThemeItem(AppTheme.Light, LocalizationService.Text("settings.themeLight")));
                ThemeCombo.SelectedIndex = idx;
            }

            var closeIdx = CloseActionCombo.SelectedIndex;
            CloseActionCombo.Items.Clear();
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Ask, LocalizationService.Text("settings.closeAsk")));
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.HideToTray, LocalizationService.Text("settings.closeTray")));
            CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Exit, LocalizationService.Text("settings.closeExit")));
            CloseActionCombo.SelectedIndex = closeIdx;
            RefreshInstallButton();
        }
        finally
        {
            _suppress = false;
        }
    }

    private async void CloseActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CloseActionCombo.SelectedItem is not CloseItem item)
            return;

        _app.Ui.CloseAction = item.Value;
        await SaveAsync();
    }

    private void Logs_Click(object sender, RoutedEventArgs e) => _openLogs();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IsEnabled = false;
            await _checkUpdates();
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (AppInstallService.IsRunningFromInstallDirectory())
            return;

        var confirm = AppMessageBox.Show(
            this,
            LocalizationService.Text("install.confirm", AppInstallService.RecommendedInstallDirectory),
            LocalizationService.Text("settings.install"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            IsEnabled = false;
            AppInstallService.Install(createDesktopShortcut: true);
            _app.Ui.InstallPromptCompleted = true;
            await SaveAsync();

            var relaunch = AppMessageBox.Show(
                this,
                LocalizationService.Text("install.doneRelaunch"),
                LocalizationService.Text("settings.install"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (relaunch == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
                AppInstallService.LaunchInstalledAndExit();
                return;
            }

            RefreshInstallButton();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Install", "Manual install failed", ex);
            AppMessageBox.Show(this,
                LocalizationService.Text("install.failed", ex.Message),
                LocalizationService.Text("settings.install"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _settings.SaveAsync(_app);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Settings", "Failed to save from Settings window", ex);
        }
    }

    private sealed record ThemeItem(AppTheme Theme, string Label);
    private sealed record CloseItem(CloseActionPreference Value, string Label);
}
