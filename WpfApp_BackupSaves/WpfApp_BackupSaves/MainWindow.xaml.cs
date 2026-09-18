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
    private bool _updateCheckStarted;
    private AppSettings _app = new();
    private readonly IUpdateChecker _updateChecker = new GitHubReleaseUpdateChecker();

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
        _vm.Status = $"Логи: {AppLog.Default.LogDirectory}";
        AppLog.Default.Info("App",
            $"MainWindow loaded; v={AppVersion.Current}; profiles={_app.Profiles.Count}; logDir={AppLog.Default.LogDirectory}");

        if (!_updateCheckStarted)
        {
            _updateCheckStarted = true;
            _ = CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var release = await _updateChecker.GetNewerReleaseAsync();
            if (release is null)
                return;

            if (string.Equals(_app.Ui.SkippedUpdateVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Default.Info("Update", $"Skipped version {release.Version} (user choice)");
                return;
            }

            // Already scheduled for exit
            if (string.Equals(_app.Ui.PendingUpdateVersion, release.Version, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_app.Ui.PendingUpdateZipPath)
                && File.Exists(_app.Ui.PendingUpdateZipPath))
            {
                _vm.Status = $"Обновление {release.Version} будет установлено при выходе";
                return;
            }

            var choice = UpdateChoice.LaterAskAgain;
            await Dispatcher.InvokeAsync(() =>
            {
                var dlg = new UpdateAvailableWindow(AppVersion.Current, release.Version, release.ReleaseNotes)
                {
                    Owner = this
                };
                dlg.ShowDialog();
                choice = dlg.Choice;
            });

            switch (choice)
            {
                case UpdateChoice.UpdateNow:
                    await DownloadAndApplyNowAsync(release);
                    break;
                case UpdateChoice.UpdateOnClose:
                    await DownloadForDeferredUpdateAsync(release);
                    break;
                case UpdateChoice.SkipThisVersion:
                    _app.Ui.SkippedUpdateVersion = release.Version;
                    _app.Ui.PendingUpdateVersion = null;
                    _app.Ui.PendingUpdateZipPath = null;
                    await _settings.SaveAsync(_app);
                    AppLog.Default.Info("Update", $"User skipped version {release.Version}");
                    _vm.Status = $"Версия {release.Version} пропущена";
                    break;
                default:
                    AppLog.Default.Info("Update", "User postponed update prompt");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Update check failed", ex);
            _vm.Status = "Проверка обновлений не удалась";
        }
    }

    private async Task DownloadAndApplyNowAsync(ReleaseInfo release)
    {
        _vm.IsBusy = true;
        _vm.Status = $"Скачивание {release.Version}…";
        try
        {
            var progress = new Progress<double>(p =>
                _vm.Status = $"Скачивание {release.Version}… {(int)(p * 100)}%");
            var zip = await UpdateInstaller.DownloadAsync(release, progress);
            _app.Ui.PendingUpdateVersion = null;
            _app.Ui.PendingUpdateZipPath = null;
            await _settings.SaveAsync(_app);

            AppLog.Default.Info("Update", $"Applying now → {release.Version}");
            _reallyClose = true;
            CleanupOnExit();
            UpdateInstaller.ApplyAndExit(zip, restart: true);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Update now failed", ex);
            MessageBox.Show(this, $"Не удалось обновить:\n{ex.Message}", "Обновление",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _vm.IsBusy = false;
        }
    }

    private async Task DownloadForDeferredUpdateAsync(ReleaseInfo release)
    {
        _vm.IsBusy = true;
        _vm.Status = $"Скачивание {release.Version} (установится при выходе)…";
        try
        {
            var progress = new Progress<double>(p =>
                _vm.Status = $"Скачивание {release.Version}… {(int)(p * 100)}%");
            var zip = await UpdateInstaller.DownloadAsync(release, progress);
            _app.Ui.PendingUpdateVersion = release.Version;
            _app.Ui.PendingUpdateZipPath = zip;
            await _settings.SaveAsync(_app);
            AppLog.Default.Info("Update", $"Deferred update ready: {release.Version} @ {zip}");
            _vm.Status = $"Обновление {release.Version} установится при выходе";
            MessageBox.Show(this,
                $"Версия {release.Version} скачана.\nОна будет установлена при выходе из приложения (без автозапуска).",
                "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Deferred download failed", ex);
            MessageBox.Show(this, $"Не удалось скачать обновление:\n{ex.Message}", "Обновление",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
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

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = AppLog.Default.LogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{dir}\"",
                UseShellExecute = true
            });
            _vm.Status = $"Логи: {dir}";
            AppLog.Default.Info("App", $"Opened log folder: {dir}");
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Open logs folder failed", ex);
            MessageBox.Show(this, $"Не удалось открыть папку логов:\n{ex.Message}", "Логи",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            TryApplyPendingUpdateOnExit();
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
                TryApplyPendingUpdateOnExit();
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

    private void TryApplyPendingUpdateOnExit()
    {
        var zip = _app.Ui.PendingUpdateZipPath;
        var ver = _app.Ui.PendingUpdateVersion;
        if (string.IsNullOrWhiteSpace(zip) || string.IsNullOrWhiteSpace(ver))
            return;

        if (!File.Exists(zip))
        {
            AppLog.Default.Warn("Update", $"Pending zip missing: {zip}");
            return;
        }

        try
        {
            // clear pending so next launch doesn't re-apply
            _app.Ui.PendingUpdateZipPath = null;
            _app.Ui.PendingUpdateVersion = null;
            _settings.Save(_app);

            AppLog.Default.Info("Update", $"Applying deferred update {ver} on exit (no restart)");
            UpdateInstaller.ApplyAndExit(zip, restart: false);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Deferred apply failed", ex);
        }
    }
}
