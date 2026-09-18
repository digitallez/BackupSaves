using System.Diagnostics;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using BackupSaves.Core.IO;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using BackupSaves.Scheduler.Services;
using WpfApp_BackupSaves.Dialogs;
using WpfApp_BackupSaves.Services;
using WpfApp_BackupSaves.ViewModels;
using WinForms = System.Windows.Forms;

namespace WpfApp_BackupSaves;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly ISettingsStore _settings = new SettingsStore();
    private readonly IBackupRunner _runner = new BackupRunner();
    private readonly IRestoreService _restore = new RestoreService();
    private readonly IWindowsTaskSchedulerService _scheduler = new WindowsTaskSchedulerService();
    private readonly ArchiveFolderWatcher _watcher;
    private WinForms.NotifyIcon? _tray;
    private bool _reallyClose;
    private bool _cleanedUp;
    private AppSettings _app = new();

    public MainWindow()
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        Title = $"BackupSaves {AppVersion.Current}";
        DataContext = _vm;
        _watcher = new ArchiveFolderWatcher(() => Dispatcher.Invoke(RefreshArchives));
        InitTray();
        Loaded += async (_, _) => await LoadAsync();
    }

    private void InitTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = $"BackupSaves {AppVersion.Current}",
            Icon = LoadAppIcon() ?? System.Drawing.SystemIcons.Application
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            AppLog.Default.Info("App", "Выход из трея");
            _reallyClose = true;
            Close();
        });
        _tray.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream is null)
                return null;
            return new System.Drawing.Icon(streamInfo.Stream);
        }
        catch
        {
            try
            {
                var exe = Environment.ProcessPath;
                return exe is null ? null : System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch
            {
                return null;
            }
        }
    }

    private async Task LoadAsync()
    {
        _app = await _settings.LoadAsync();
        ThemeManager.Apply(_app.Ui.Theme);
        UpdateThemeToggleCaption();
        ReloadProfilesUi();
        ReloadHistoryUi();
        _watcher.Watch(_app.Profiles);
        _vm.Status = $"Настройки: {_settings.SettingsPath}";
        AppLog.Default.Info("App",
            $"MainWindow loaded; v={AppVersion.Current}; profiles={_app.Profiles.Count}; logDir={AppLog.Default.LogDirectory}");
    }

    private async void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeManager.Toggle();
        _app.Ui.Theme = next;
        UpdateThemeToggleCaption();
        AppLog.Default.Info("Settings", $"Смена темы → {next}");
        await _settings.SaveAsync(_app);
        _vm.Status = next == AppTheme.Dark ? "Тема: тёмная" : "Тема: светлая";
    }

    private void UpdateThemeToggleCaption()
    {
        // Button offers the *other* theme
        ThemeToggleButton.Content = ThemeManager.Current == AppTheme.Dark
            ? "☀ Светлая"
            : "🌙 Тёмная";
    }

    private void ReloadProfilesUi()
    {
        var selectedId = _vm.SelectedProfile?.Id;
        _vm.Profiles.Clear();
        foreach (var p in _app.Profiles.OrderBy(p => p.Name))
            _vm.Profiles.Add(p);
        _vm.SelectedProfile = selectedId is Guid id
            ? _vm.Profiles.FirstOrDefault(p => p.Id == id)
            : _vm.Profiles.FirstOrDefault();
    }

    private void ReloadHistoryUi()
    {
        _vm.History.Clear();
        foreach (var h in _app.History.Take(50))
            _vm.History.Add(h);
    }

    private void Profiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshArchives();
    }

    private void RefreshArchives()
    {
        var profile = _vm.SelectedProfile;
        var selectedPath = _vm.SelectedArchive?.Path;
        _vm.Archives.Clear();
        if (profile is null || string.IsNullOrWhiteSpace(profile.BackupRoot))
            return;

        var dir = PathHelper.GetProfileArchiveDirectory(profile);
        if (!Directory.Exists(dir))
            return;

        var items = Directory.EnumerateFiles(dir)
            .Where(f =>
            {
                var n = f.ToLowerInvariant();
                return (n.EndsWith(".7z") || n.EndsWith(".zip")) && !n.EndsWith(".tmp");
            })
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new ArchiveListItem
            {
                Path = f.FullName,
                Name = f.Name,
                LastWriteTime = f.LastWriteTime,
                SizeBytes = f.Length
            });

        foreach (var item in items)
            _vm.Archives.Add(item);

        _vm.SelectedArchive = selectedPath is not null
            ? _vm.Archives.FirstOrDefault(a => a.Path == selectedPath)
            : _vm.Archives.FirstOrDefault();
    }

    private async Task PersistAndSyncSchedulerAsync(BackupProfile? changed = null)
    {
        await _settings.SaveAsync(_app);
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe))
            return;

        var targets = changed is null ? _app.Profiles : [_app.Profiles.First(p => p.Id == changed.Id)];
        foreach (var p in targets)
        {
            try
            {
                _scheduler.Upsert(p, exe);
                AppLog.Default.Info("Scheduler",
                    $"Upsert task for «{p.Name}» enabled={p.Schedule.Enabled} kind={p.Schedule.Kind}");
            }
            catch (Exception ex)
            {
                AppLog.Default.Error("Scheduler", $"Upsert failed for «{p.Name}»", ex);
                _vm.Status = $"Scheduler: {ex.Message}";
            }
        }

        _watcher.Watch(_app.Profiles);
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileEditWindow { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _app.Profiles.Add(dlg.Profile);
        AppLog.Default.Info("Settings",
            $"Профиль создан: «{dlg.Profile.Name}» id={dlg.Profile.Id:N} format={dlg.Profile.Format} sources={dlg.Profile.Sources.Count} root=\"{dlg.Profile.BackupRoot}\"");
        await PersistAndSyncSchedulerAsync(dlg.Profile);
        ReloadProfilesUi();
        _vm.SelectedProfile = _vm.Profiles.FirstOrDefault(p => p.Id == dlg.Profile.Id);
        _vm.Status = $"Профиль «{dlg.Profile.Name}» создан";
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var dlg = new ProfileEditWindow(_vm.SelectedProfile) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var idx = _app.Profiles.FindIndex(p => p.Id == dlg.Profile.Id);
        if (idx < 0) return;
        _app.Profiles[idx] = dlg.Profile;
        AppLog.Default.Info("Settings",
            $"Профиль изменён: «{dlg.Profile.Name}» id={dlg.Profile.Id:N} format={dlg.Profile.Format} sources={dlg.Profile.Sources.Count} schedule={dlg.Profile.Schedule.Enabled}/{dlg.Profile.Schedule.Kind}");
        await PersistAndSyncSchedulerAsync(dlg.Profile);
        ReloadProfilesUi();
        _vm.SelectedProfile = _vm.Profiles.FirstOrDefault(p => p.Id == dlg.Profile.Id);
        _vm.Status = $"Профиль «{dlg.Profile.Name}» сохранён";
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var p = _vm.SelectedProfile;
        if (MessageBox.Show(this, $"Удалить профиль «{p.Name}»? Архивы на диске не трогаем.", "BackupSaves",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try { _scheduler.Delete(p); } catch { /* ignore */ }
        AppLog.Default.Info("Settings", $"Профиль удалён: «{p.Name}» id={p.Id:N}");
        _app.Profiles.RemoveAll(x => x.Id == p.Id);
        await _settings.SaveAsync(_app);
        ReloadProfilesUi();
        _watcher.Watch(_app.Profiles);
        RefreshArchives();
        _vm.Status = "Профиль удалён";
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null || _vm.IsBusy) return;
        _vm.IsBusy = true;
        _vm.Status = "Бэкап…";
        try
        {
            var result = await _runner.RunProfileAsync(_vm.SelectedProfile.Id, RunTrigger.Manual);
            _app = await _settings.LoadAsync();
            ReloadHistoryUi();
            RefreshArchives();
            _vm.Status = result.Success
                ? $"OK: {result.ArchivePath}"
                : $"Ошибка: {result.ErrorMessage}";
            if (!result.Success)
                MessageBox.Show(this, result.ErrorMessage, "Бэкап", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedArchive is null || _vm.IsBusy) return;

        var confirm = MessageBox.Show(this,
            $"Восстановить все файлы из «{_vm.SelectedArchive.Name}» в исходные папки?\nСуществующие файлы будут перезаписаны.",
            "Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            AppLog.Default.Info("Restore", "Пользователь отменил confirm");
            return;
        }

        AppLog.Default.Info("Restore", $"UI restore: \"{_vm.SelectedArchive.Path}\"");
        _vm.IsBusy = true;
        _vm.Status = "Restore…";
        try
        {
            var result = await _restore.RestoreAsync(_vm.SelectedArchive.Path, overwrite: true);
            if (result.Success)
            {
                _vm.Status = $"Restore OK: {result.RestoredCount} файлов";
                MessageBox.Show(this, $"Восстановлено файлов: {result.RestoredCount}", "Restore",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var details = result.ErrorMessage ?? "";
                if (result.Errors.Count > 0)
                    details += "\n\n" + string.Join("\n", result.Errors.Take(15).Select(x => $"{x.SourcePath}: {x.Message}"));
                _vm.Status = details.Split('\n')[0];
                MessageBox.Show(this, details, "Restore — ошибки", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var dir = PathHelper.GetProfileArchiveDirectory(_vm.SelectedProfile);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _app.Ui.MinimizeToTray)
        {
            Hide();
            _tray!.ShowBalloonTip(1500, "BackupSaves", "Свёрнуто в трей", WinForms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyClose)
        {
            CleanupOnExit();
            return;
        }

        // Cannot Close() from inside Closing — ask, then either keep Cancel or allow this close.
        e.Cancel = true;
        var dlg = new CloseChoiceWindow { Owner = this };
        dlg.ShowDialog();

        switch (dlg.Choice)
        {
            case CloseChoice.HideToTray:
                AppLog.Default.Info("App", "Пользователь скрыл приложение в трей");
                Hide();
                _tray?.ShowBalloonTip(1500, "BackupSaves", "Работает в трее", WinForms.ToolTipIcon.Info);
                break;
            case CloseChoice.Exit:
                AppLog.Default.Info("App", "Пользователь подтвердил выход");
                _reallyClose = true;
                e.Cancel = false;
                CleanupOnExit();
                break;
            default:
                AppLog.Default.Info("App", "Закрытие отменено");
                break;
        }
    }

    private void CleanupOnExit()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        AppLog.Default.Info("App", "Закрытие главного окна");
        _watcher.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
