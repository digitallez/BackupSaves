using System.IO;
using System.Windows;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Dialogs;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves;

public partial class App : System.Windows.Application
{
    private bool _guiMode;
    private SingleInstanceGuard? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryGetBackupProfileId(e.Args, out var profileId))
        {
            AppLog.Default.Info("App", $"Headless start --backup {profileId:D}");
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = await RunHeadlessBackupAsync(profileId);
            AppLog.Default.Info("App", $"Headless exit code={code}");
            Shutdown(code);
            return;
        }

        var anotherGuiRunning = false;
        try
        {
            _singleInstance = SingleInstanceGuard.TryAcquirePrimary();
            anotherGuiRunning = _singleInstance is null;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Single-instance guard failed; continuing as primary", ex);
        }

        if (anotherGuiRunning)
        {
            AppLog.Default.Info("App", "Another GUI instance is running — activating it and exiting");
            Shutdown(0);
            return;
        }

        _guiMode = true;
        AppLog.Default.Info("App", $"GUI start exe=\"{Environment.ProcessPath}\" pid={Environment.ProcessId}");

        var settingsStore = new SettingsStore();
        AppSettings settings;
        try
        {
            settings = settingsStore.Load();
            ThemeManager.Apply(settings.Ui.Theme);
            var lang = LocalizationService.Instance.ResolveInitialLanguage(settings.Ui.Language);
            LocalizationService.Instance.SetLanguage(lang);
            AppLog.Default.Info("App", $"Theme from settings: {settings.Ui.Theme}; lang={LocalizationService.Instance.Language}");
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Failed to load theme, fallback Dark", ex);
            settings = new AppSettings();
            ThemeManager.Apply(AppTheme.Dark);
            var lang = LocalizationService.Instance.ResolveInitialLanguage(null);
            LocalizationService.Instance.SetLanguage(lang);
        }

        if (!TryHandleFirstRunInstall(settingsStore, settings))
            return;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _singleInstance?.StartListening(() =>
            Dispatcher.BeginInvoke(new Action(ActivateExistingMainWindow)));
    }

    /// <summary>
    /// First-run install prompt. Returns false when this process should stop
    /// (installed copy launched, or user closed the prompt without a choice).
    /// </summary>
    private bool TryHandleFirstRunInstall(ISettingsStore store, AppSettings settings)
    {
        try
        {
            if (AppInstallService.IsRunningFromInstallDirectory())
            {
                if (!settings.Ui.InstallPromptCompleted)
                {
                    settings.Ui.InstallPromptCompleted = true;
                    store.Save(settings);
                }

                return true;
            }

            // Existing users (settings.json already present) — don't re-prompt after upgrade.
            if (!settings.Ui.InstallPromptCompleted && File.Exists(store.SettingsPath))
            {
                settings.Ui.InstallPromptCompleted = true;
                store.Save(settings);
                return true;
            }

            if (settings.Ui.InstallPromptCompleted)
                return true;

            var dlg = new FirstRunInstallWindow();
            var result = dlg.ShowDialog();
            if (result != true)
            {
                // Closed via title-bar X — treat as "use current folder".
                settings.Ui.InstallPromptCompleted = true;
                store.Save(settings);
                return true;
            }

            if (!dlg.InstallChosen)
            {
                settings.Ui.InstallPromptCompleted = true;
                store.Save(settings);
                AppLog.Default.Info("Install", "First-run: user kept current folder");
                return true;
            }

            AppInstallService.Install(createDesktopShortcut: true);
            settings.Ui.InstallPromptCompleted = true;
            store.Save(settings);
            ReleaseSingleInstanceForRelaunch();
            AppInstallService.LaunchInstalledAndExit();
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("Install", "First-run install prompt failed", ex);
            try
            {
                settings.Ui.InstallPromptCompleted = true;
                store.Save(settings);
            }
            catch
            {
                // ignore
            }

            return true;
        }
    }

    /// <summary>Release the GUI single-instance mutex before launching another copy.</summary>
    internal void ReleaseSingleInstanceForRelaunch()
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_guiMode)
            AppLog.Default.Info("App", $"GUI exit code={e.ApplicationExitCode}");
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void ActivateExistingMainWindow()
    {
        if (MainWindow is MainWindow mw)
        {
            mw.BringToForeground();
            return;
        }

        if (MainWindow is null)
            return;

        MainWindow.Show();
        MainWindow.Activate();
    }

    private static bool TryGetBackupProfileId(string[] args, out Guid profileId)
    {
        profileId = Guid.Empty;
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--backup", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= args.Length)
                return false;
            return Guid.TryParse(args[i + 1], out profileId);
        }

        return false;
    }

    private static async Task<int> RunHeadlessBackupAsync(Guid profileId)
    {
        try
        {
            var runner = new BackupRunner();
            var result = await runner.RunProfileAsync(profileId, RunTrigger.Scheduler);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Headless backup failed", ex);
            return 2;
        }
    }
}
