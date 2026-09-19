using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
    private readonly ISettingsStore _settings;
    private readonly IHistoryStore _historyStore;
    private readonly IBackupRunner _runner;
    private readonly IRestoreService _restore = new RestoreService();
    private readonly IWindowsTaskSchedulerService _scheduler = new WindowsTaskSchedulerService();
    private readonly ArchiveFolderWatcher _watcher;
    private WinForms.NotifyIcon? _tray;
    private bool _reallyClose;
    private bool _cleanedUp;
    private bool _updateCheckStarted;
    private AppSettings _app = new();
    private bool _historyFullyLoaded;
    private int _historyHiddenCount;
    private readonly IUpdateChecker _updateChecker = new GitHubReleaseUpdateChecker();
    private InAppBackupScheduler? _inAppScheduler;
    private DispatcherTimer? _profilesLiveTimer;
    private readonly Dictionary<Guid, DateTimeOffset?> _taskNextRunCache = new();
    private DateTimeOffset _taskNextRunCacheAt = DateTimeOffset.MinValue;
    private readonly Dictionary<Guid, bool> _watchRunningCache = new();
    private int _profilesLiveGate;

    public MainWindow()
    {
        _settings = new SettingsStore();
        _historyStore = new HistoryStore(_settings);
        _runner = new BackupRunner(_settings, _historyStore);

        InitializeComponent();
        CustomWindowChrome.Apply(this);
        Title = $"BackupSaves {AppVersion.Current}";
        DataContext = _vm;
        _watcher = new ArchiveFolderWatcher(() => Dispatcher.Invoke(RefreshArchives));
        InitTray();
        _vm.LanguageChanged += OnUiLanguageChanged;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                RebuildTrayMenu();
                UpdateThemeToggleCaption();
                RefreshProfilesLive();
                RefreshHistoryLoadMoreCaption();
            });
        };
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void OnUiLanguageChanged(object? sender, string culture)
    {
        _app.Ui.Language = culture;
        try
        {
            await _settings.SaveAsync(_app);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Settings", "Failed to save language", ex);
        }
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
        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.Text("tray.open"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(LocalizationService.Text("tray.exit"), null, (_, _) =>
        {
            AppLog.Default.Info("App", "Exit from tray");
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
        var lang = LocalizationService.Instance.ResolveInitialLanguage(_app.Ui.Language);
        LocalizationService.Instance.SetLanguage(lang);
        _vm.ReloadLanguages();
        _vm.SelectLanguageSilent(LocalizationService.Instance.Language);
        UpdateThemeToggleCaption();
        RebuildTrayMenu();
        ReloadProfilesUi();
        await ReloadHistoryUiAsync();
        _watcher.Watch(_app.Profiles);
        _vm.Status = LocalizationService.Text("status.logs", AppLog.Default.LogDirectory);
        AppLog.Default.Info("App",
            $"MainWindow loaded; v={AppVersion.Current}; profiles={_app.Profiles.Count}; logDir={AppLog.Default.LogDirectory}; lang={LocalizationService.Instance.Language}");

        EnsureInAppScheduler();
        _inAppScheduler!.Start();
        StartProfilesLiveTimer();

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
                _vm.Status = LocalizationService.Text("update.statusWillInstall", release.Version);
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
                    _vm.Status = LocalizationService.Text("update.statusSkipped", release.Version);
                    break;
                default:
                    AppLog.Default.Info("Update", "User postponed update prompt");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Update check failed", ex);
            _vm.Status = LocalizationService.Text("update.statusCheckFailed");
        }
    }

    private async Task DownloadAndApplyNowAsync(ReleaseInfo release)
    {
        _vm.IsBusy = true;
        _vm.Status = LocalizationService.Text("update.statusDownloading", release.Version);
        try
        {
            var progress = new Progress<double>(p =>
                _vm.Status = LocalizationService.Text("update.statusDownloadingPct", release.Version, (int)(p * 100)));
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
            MessageBox.Show(this, LocalizationService.Text("update.failed", ex.Message),
                LocalizationService.Text("update.title"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            _vm.IsBusy = false;
        }
    }

    private async Task DownloadForDeferredUpdateAsync(ReleaseInfo release)
    {
        _vm.IsBusy = true;
        _vm.Status = LocalizationService.Text("update.statusDownloadingDeferred", release.Version);
        try
        {
            var progress = new Progress<double>(p =>
                _vm.Status = LocalizationService.Text("update.statusDownloadingPct", release.Version, (int)(p * 100)));
            var zip = await UpdateInstaller.DownloadAsync(release, progress);
            _app.Ui.PendingUpdateVersion = release.Version;
            _app.Ui.PendingUpdateZipPath = zip;
            await _settings.SaveAsync(_app);
            AppLog.Default.Info("Update", $"Deferred update ready: {release.Version} @ {zip}");
            _vm.Status = LocalizationService.Text("update.statusDeferredReady", release.Version);
            MessageBox.Show(this,
                LocalizationService.Text("update.deferredReady", release.Version),
                LocalizationService.Text("update.title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Update", "Deferred download failed", ex);
            MessageBox.Show(this, LocalizationService.Text("update.downloadFailed", ex.Message),
                LocalizationService.Text("update.title"),
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
        AppLog.Default.Info("Settings", $"Theme → {next}");
        await _settings.SaveAsync(_app);
        _vm.Status = next == AppTheme.Dark
            ? LocalizationService.Text("status.themeDark")
            : LocalizationService.Text("status.themeLight");
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
            _vm.Status = LocalizationService.Text("status.logs", dir);
            AppLog.Default.Info("App", $"Opened log folder: {dir}");
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Open logs folder failed", ex);
            MessageBox.Show(this, LocalizationService.Text("msg.logsOpenFailed", ex.Message),
                LocalizationService.Text("msg.logsTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateThemeToggleCaption()
    {
        // Button offers the *other* theme
        ThemeToggleButton.Content = ThemeManager.Current == AppTheme.Dark
            ? LocalizationService.Text("main.themeLight")
            : LocalizationService.Text("main.themeDark");
    }

    private void ReloadProfilesUi()
    {
        var selectedId = _vm.SelectedProfile?.Id;
        _vm.Profiles.Clear();
        foreach (var p in _app.Profiles.OrderBy(p => p.Name))
            _vm.Profiles.Add(new ProfileListItem(p));
        _vm.SelectedProfile = selectedId is Guid id
            ? _vm.Profiles.FirstOrDefault(p => p.Id == id)
            : _vm.Profiles.FirstOrDefault();
        InvalidateTaskNextRunCache();
        RefreshProfilesLive();
    }

    private void StartProfilesLiveTimer()
    {
        if (_profilesLiveTimer is not null)
            return;

        _profilesLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _profilesLiveTimer.Tick += (_, _) => RefreshProfilesLive();
        _profilesLiveTimer.Start();
        RefreshProfilesLive();
    }

    private void InvalidateTaskNextRunCache()
    {
        _taskNextRunCache.Clear();
        _taskNextRunCacheAt = DateTimeOffset.MinValue;
        _watchRunningCache.Clear();
    }

    private void RefreshProfilesLive()
    {
        if (_vm.Profiles.Count == 0)
            return;

        _ = RefreshProfilesLiveAsync();
    }

    private async Task RefreshProfilesLiveAsync()
    {
        if (_vm.Profiles.Count == 0)
            return;

        if (Interlocked.CompareExchange(ref _profilesLiveGate, 1, 0) != 0)
            return;

        try
        {
            var now = DateTimeOffset.Now;
            EnsureTaskNextRunCache(now);
            await EnsureWatchRunningCacheAsync();

            foreach (var item in _vm.Profiles)
            {
                var profile = _app.Profiles.FirstOrDefault(p => p.Id == item.Id) ?? item.Profile;
                _taskNextRunCache.TryGetValue(profile.Id, out var next);
                _watchRunningCache.TryGetValue(profile.Id, out var running);
                item.Refresh(profile, now, next, running);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _profilesLiveGate, 0);
        }
    }

    private Task EnsureWatchRunningCacheAsync()
    {
        var shortest = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        var profilePatterns = new List<(Guid Id, string? Pattern, bool WatchOn)>();

        foreach (var profile in _app.Profiles)
        {
            var watchOn = profile.WatchProcessEnabled
                          && ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern);
            profilePatterns.Add((profile.Id, profile.WatchProcessPattern, watchOn));
            if (!watchOn)
                continue;

            var key = profile.WatchProcessPattern!.Trim();
            var interval = TimeSpan.FromSeconds(
                Math.Max(1, profile.WatchProcessScanSeconds <= 0 ? 10 : profile.WatchProcessScanSeconds));
            if (!shortest.TryGetValue(key, out var existing) || interval < existing)
                shortest[key] = interval;
        }

        return ApplyWatchRunningCacheAsync(shortest, profilePatterns);
    }

    private async Task ApplyWatchRunningCacheAsync(
        Dictionary<string, TimeSpan> shortest,
        List<(Guid Id, string? Pattern, bool WatchOn)> profilePatterns)
    {
        var runningMap = shortest.Count == 0
            ? (IReadOnlyDictionary<string, bool>)new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            : await Task.Run(() => ProcessWatchService.EvaluatePatterns(
                shortest.Select(kv => (kv.Key, (TimeSpan?)kv.Value)))).ConfigureAwait(true);

        var liveIds = new HashSet<Guid>();
        foreach (var (id, pattern, watchOn) in profilePatterns)
        {
            liveIds.Add(id);
            if (!watchOn)
            {
                _watchRunningCache[id] = false;
                continue;
            }

            _watchRunningCache[id] = pattern is not null
                && runningMap.TryGetValue(pattern.Trim(), out var running)
                && running;
        }

        foreach (var stale in _watchRunningCache.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _watchRunningCache.Remove(stale);
    }

    private void EnsureTaskNextRunCache(DateTimeOffset now)
    {
        if (now - _taskNextRunCacheAt < TimeSpan.FromSeconds(30) && _taskNextRunCache.Count > 0)
            return;

        _taskNextRunCache.Clear();
        foreach (var profile in _app.Profiles)
        {
            DateTimeOffset? next = null;
            if (profile.Schedule.Enabled)
            {
                try { next = _scheduler.GetNextRunTime(profile); }
                catch { /* ignore */ }
            }

            _taskNextRunCache[profile.Id] = next;
        }

        _taskNextRunCacheAt = now;
    }

    private async Task ReloadHistoryUiAsync(bool loadAll = false)
    {
        var all = await _historyStore.LoadAsync();
        if (loadAll)
            _historyFullyLoaded = true;

        var take = _historyFullyLoaded
            ? all.Count
            : Math.Min(HistoryStore.UiInitialCount, all.Count);

        _vm.History.Clear();
        for (var i = 0; i < take; i++)
        {
            var h = all[i];
            if (string.IsNullOrWhiteSpace(h.ProfileName))
            {
                h.ProfileName = _app.Profiles.FirstOrDefault(p => p.Id == h.ProfileId)?.Name
                               ?? LocalizationService.Text("history.profileDeleted");
            }

            _vm.History.Add(h);
        }

        if (!_historyFullyLoaded && all.Count > take)
        {
            _historyHiddenCount = all.Count - take;
            _vm.History.Add(new HistoryLoadMoreItem
            {
                Caption = LocalizationService.Text("history.loadAll", _historyHiddenCount)
            });
        }
        else
        {
            _historyHiddenCount = 0;
        }
    }

    private void RefreshHistoryLoadMoreCaption()
    {
        if (_historyHiddenCount <= 0) return;
        for (var i = 0; i < _vm.History.Count; i++)
        {
            if (_vm.History[i] is not HistoryLoadMoreItem) continue;
            _vm.History[i] = new HistoryLoadMoreItem
            {
                Caption = LocalizationService.Text("history.loadAll", _historyHiddenCount)
            };
            return;
        }
    }

    private async void LoadAllHistory_Click(object sender, RoutedEventArgs e)
    {
        await ReloadHistoryUiAsync(loadAll: true);
    }

    private void Profiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshArchives();
    }

    private void RefreshArchives()
    {
        var profile = _vm.SelectedProfile?.Profile;
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
        EnsureInAppScheduler();
        _inAppScheduler!.Start();
        InvalidateTaskNextRunCache();
        RefreshProfilesLive();
    }

    private void EnsureInAppScheduler()
    {
        if (_inAppScheduler is not null)
            return;

        _inAppScheduler = new InAppBackupScheduler(
            getApp: () => _app,
            runBackup: RunInAppBackupAsync,
            saveApp: () => _settings.SaveAsync(_app),
            setStatus: s => _vm.Status = s);
    }

    private async Task RunInAppBackupAsync(Guid profileId)
    {
        if (_vm.IsBusy)
        {
            AppLog.Default.Info("InAppSchedule", $"Skip {profileId:N}: UI busy");
            return;
        }

        _vm.IsBusy = true;
        try
        {
            var result = await _runner.RunProfileAsync(profileId, RunTrigger.InApp);
            _app = await _settings.LoadAsync();

            var profile = _app.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is not null)
            {
                profile.Schedule.LastInAppBackupUtc = DateTimeOffset.UtcNow;
                await _settings.SaveAsync(_app);
            }

            await ReloadHistoryUiAsync();
            RefreshArchives();
            RefreshProfilesLive();
            _vm.Status = result.Skipped
                ? (result.StatusMessage ?? LocalizationService.Text("status.skipDefault"))
                : result.Success
                    ? LocalizationService.Text("status.autoBackupOk", result.ArchivePath)
                    : LocalizationService.Text("status.autoBackupError", result.ErrorMessage);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileEditWindow { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _app.Profiles.Add(dlg.Profile);
        AppLog.Default.Info("Settings",
            $"Profile created: «{dlg.Profile.Name}» id={dlg.Profile.Id:N} format={dlg.Profile.Format} sources={dlg.Profile.Sources.Count} root=\"{dlg.Profile.BackupRoot}\"");
        await PersistAndSyncSchedulerAsync(dlg.Profile);
        EnsureInAppScheduler();
        _inAppScheduler!.Start();
        ReloadProfilesUi();
        _vm.SelectedProfile = _vm.Profiles.FirstOrDefault(p => p.Id == dlg.Profile.Id);
        _vm.Status = LocalizationService.Text("status.profileCreated", dlg.Profile.Name);
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var dlg = new ProfileEditWindow(_vm.SelectedProfile.Profile) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var idx = _app.Profiles.FindIndex(p => p.Id == dlg.Profile.Id);
        if (idx < 0) return;
        _app.Profiles[idx] = dlg.Profile;
        AppLog.Default.Info("Settings",
            $"Profile updated: «{dlg.Profile.Name}» id={dlg.Profile.Id:N} format={dlg.Profile.Format} sources={dlg.Profile.Sources.Count} schedule={dlg.Profile.Schedule.Enabled}/{dlg.Profile.Schedule.Kind} inApp={dlg.Profile.Schedule.InAppEnabled}");
        await PersistAndSyncSchedulerAsync(dlg.Profile);
        EnsureInAppScheduler();
        _inAppScheduler!.Start();
        ReloadProfilesUi();
        _vm.SelectedProfile = _vm.Profiles.FirstOrDefault(p => p.Id == dlg.Profile.Id);
        _vm.Status = LocalizationService.Text("status.profileSaved", dlg.Profile.Name);
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var p = _vm.SelectedProfile.Profile;
        if (MessageBox.Show(this, LocalizationService.Text("msg.deleteProfile", p.Name),
                LocalizationService.Text("common.appName"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try { _scheduler.Delete(p); } catch { /* ignore */ }
        AppLog.Default.Info("Settings", $"Profile deleted: «{p.Name}» id={p.Id:N}");
        _app.Profiles.RemoveAll(x => x.Id == p.Id);
        await _settings.SaveAsync(_app);
        ReloadProfilesUi();
        _watcher.Watch(_app.Profiles);
        RefreshArchives();
        _vm.Status = LocalizationService.Text("status.profileDeleted");
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null || _vm.IsBusy) return;
        _vm.IsBusy = true;
        _vm.Status = LocalizationService.Text("status.backup");
        try
        {
            var result = await _runner.RunProfileAsync(_vm.SelectedProfile.Id, RunTrigger.Manual);
            _app = await _settings.LoadAsync();
            await ReloadHistoryUiAsync();
            RefreshArchives();
            RefreshProfilesLive();
            _vm.Status = result.Skipped
                ? (result.StatusMessage ?? LocalizationService.Text("status.skipDefault"))
                : result.Success
                    ? LocalizationService.Text("status.backupOk", result.ArchivePath)
                    : LocalizationService.Text("status.backupError", result.ErrorMessage);
            if (!result.Success)
                MessageBox.Show(this, result.ErrorMessage, LocalizationService.Text("msg.backupTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        await RestoreSelectedArchiveAsync();
    }

    private async Task RestoreSelectedArchiveAsync()
    {
        if (_vm.SelectedArchive is null || _vm.IsBusy) return;

        var confirm = MessageBox.Show(this,
            LocalizationService.Text("msg.restoreConfirm", _vm.SelectedArchive.Name),
            LocalizationService.Text("msg.restoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            AppLog.Default.Info("Restore", "User cancelled confirm");
            return;
        }

        AppLog.Default.Info("Restore", $"UI restore: \"{_vm.SelectedArchive.Path}\"");
        _vm.IsBusy = true;
        _vm.Status = LocalizationService.Text("status.restoring");
        try
        {
            var result = await _restore.RestoreAsync(_vm.SelectedArchive.Path, overwrite: true);
            if (result.Success)
            {
                _vm.Status = LocalizationService.Text("status.restored", result.RestoredCount);
                MessageBox.Show(this, LocalizationService.Text("msg.restoreOk", result.RestoredCount),
                    LocalizationService.Text("msg.restoreTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var details = result.ErrorMessage ?? "";
                if (result.Errors.Count > 0)
                    details += "\n\n" + string.Join("\n", result.Errors.Take(15).Select(x => $"{x.SourcePath}: {x.Message}"));
                _vm.Status = details.Split('\n')[0];
                MessageBox.Show(this, details, LocalizationService.Text("msg.restoreErrors"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void ArchivesList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null && dep is not System.Windows.Controls.ListBoxItem)
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);

        if (dep is System.Windows.Controls.ListBoxItem item)
            item.IsSelected = true;
    }

    private void ArchiveRevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedArchive is null) return;
        var path = _vm.SelectedArchive.Path;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, LocalizationService.Text("msg.archiveMissing"),
                LocalizationService.Text("msg.archivesTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshArchives();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            AppLog.Default.Info("App", $"Reveal in explorer: \"{path}\"");
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Reveal in explorer failed", ex);
            MessageBox.Show(this, LocalizationService.Text("msg.explorerFailed", ex.Message),
                LocalizationService.Text("msg.archivesTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ArchiveDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedArchive is null || _vm.IsBusy) return;

        var name = _vm.SelectedArchive.Name;
        var path = _vm.SelectedArchive.Path;
        if (MessageBox.Show(this,
                LocalizationService.Text("msg.deleteArchive", name),
                LocalizationService.Text("msg.deleteArchiveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
            AppLog.Default.Info("App", $"Archive deleted: \"{path}\"");
            _vm.Status = LocalizationService.Text("status.deleted", name);
            RefreshArchives();
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", $"Delete archive failed: \"{path}\"", ex);
            MessageBox.Show(this, LocalizationService.Text("msg.deleteArchiveFailed", ex.Message),
                LocalizationService.Text("msg.deleteArchiveTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var dir = PathHelper.GetProfileArchiveDirectory(_vm.SelectedProfile.Profile);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _app.Ui.MinimizeToTray)
        {
            Hide();
            _tray!.ShowBalloonTip(1500, "BackupSaves", LocalizationService.Text("tray.minimized"), WinForms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ForceExit_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Default.Info("App", "Force exit (History button)");
        _reallyClose = true;
        Close();
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
                AppLog.Default.Info("App", "User hid app to tray");
                Hide();
                _tray?.ShowBalloonTip(1500, "BackupSaves", LocalizationService.Text("tray.running"), WinForms.ToolTipIcon.Info);
                break;
            case CloseChoice.Exit:
                AppLog.Default.Info("App", "User confirmed exit");
                _reallyClose = true;
                e.Cancel = false;
                CleanupOnExit();
                TryApplyPendingUpdateOnExit();
                break;
            default:
                AppLog.Default.Info("App", "Close cancelled");
                break;
        }
    }

    private void CleanupOnExit()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        AppLog.Default.Info("App", "Main window closing");
        if (_profilesLiveTimer is not null)
        {
            _profilesLiveTimer.Stop();
            _profilesLiveTimer = null;
        }
        _inAppScheduler?.Dispose();
        _inAppScheduler = null;
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
