 using System.Diagnostics;
 using System.IO;
 using System.Windows;
 using System.Windows.Controls;
 using System.Windows.Documents;
 using BackupSaves.Core.Models;
 using BackupSaves.Core.Services;
 using WpfApp_BackupSaves.Services;
 using WpfApp_BackupSaves.ViewModels;
 using WinForms = System.Windows.Forms;

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
            FillThemeCombo();
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

            FillCloseActionCombo();
            CloseActionCombo.SelectedItem = CloseActionCombo.Items.Cast<CloseItem>()
                .FirstOrDefault(c => c.Value == _app.Ui.CloseAction)
                ?? CloseActionCombo.Items[0];

            CheckUpdatesAutoBox.IsChecked = _app.Ui.CheckForUpdates;

            RefreshInstallButton();
            LoadDevFolderPanel();
        }
        finally
        {
            _suppress = false;
        }
    }

    private void LoadDevFolderPanel()
    {
        DevFolderPanel.Visibility = Visibility.Visible;
        DevFolderBox.Text = _app.Ui.DevBuildFolder ?? "";
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
            var themeIdx = ThemeCombo.SelectedIndex;
            FillThemeCombo();
            ThemeCombo.SelectedIndex = themeIdx;

            var closeIdx = CloseActionCombo.SelectedIndex;
            FillCloseActionCombo();
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

    private async void CheckUpdatesAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;

        _app.Ui.CheckForUpdates = CheckUpdatesAutoBox.IsChecked == true;
        await SaveAsync();
    }

    private void GitHubReleases_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubReleaseUpdateChecker.ReleasesPageUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Settings", "Open GitHub releases failed", ex);
            AppMessageBox.Show(this,
                LocalizationService.Text("settings.githubReleasesOpenFailed", ex.Message),
                LocalizationService.Text("settings.githubReleases"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private async void DevFolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = LocalizationService.Text("settings.devFolder"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(DevFolderBox.Text)
                ? DevFolderBox.Text
                : (_app.Ui.DevBuildFolder ?? "")
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK)
            return;

        DevFolderBox.Text = dlg.SelectedPath;
        await SaveDevFolderAsync();
    }

    private async void DevFolderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;
        await SaveDevFolderAsync();
    }

    private async Task SaveDevFolderAsync()
    {
        var path = DevFolderBox.Text.Trim();
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(_app.Ui.DevBuildFolder ?? "", normalized ?? "", StringComparison.OrdinalIgnoreCase))
            return;

        _app.Ui.DevBuildFolder = normalized;
        await SaveAsync();
        AppLog.Default.Info("Settings", $"DevBuildFolder set to \"{normalized}\"");
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        await SaveDevFolderAsync();
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

    private void FillThemeCombo()
    {
        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(new ThemeItem(AppTheme.Dark, LocalizationService.Text("settings.themeDark"), "\uE708"));
        ThemeCombo.Items.Add(new ThemeItem(AppTheme.Light, LocalizationService.Text("settings.themeLight"), "\uE706"));
    }

    private void FillCloseActionCombo()
    {
        CloseActionCombo.Items.Clear();
        CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Ask, LocalizationService.Text("settings.closeAsk"), "\uE897"));
        CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.HideToTray, LocalizationService.Text("settings.closeTray"), "\uE75B"));
        CloseActionCombo.Items.Add(new CloseItem(CloseActionPreference.Exit, LocalizationService.Text("settings.closeExit"), "\uE7E8"));
    }

    private sealed record ThemeItem(AppTheme Theme, string Label, string Icon);
    private sealed record CloseItem(CloseActionPreference Value, string Label, string Icon);
}
