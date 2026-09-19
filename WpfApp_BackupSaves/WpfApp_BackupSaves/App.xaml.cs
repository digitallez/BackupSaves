using System.Windows;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves;

public partial class App : System.Windows.Application
{
    private bool _guiMode;

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

        _guiMode = true;
        AppLog.Default.Info("App", $"GUI start exe=\"{Environment.ProcessPath}\" pid={Environment.ProcessId}");

        try
        {
            var settings = new SettingsStore().Load();
            ThemeManager.Apply(settings.Ui.Theme);
            var lang = LocalizationService.Instance.ResolveInitialLanguage(settings.Ui.Language);
            LocalizationService.Instance.SetLanguage(lang);
            AppLog.Default.Info("App", $"Тема из настроек: {settings.Ui.Theme}; язык={LocalizationService.Instance.Language}");
        }
        catch (Exception ex)
        {
            AppLog.Default.Error("App", "Не удалось загрузить тему, fallback Dark", ex);
            ThemeManager.Apply(AppTheme.Dark);
            var lang = LocalizationService.Instance.ResolveInitialLanguage(null);
            LocalizationService.Instance.SetLanguage(lang);
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_guiMode)
            AppLog.Default.Info("App", $"GUI exit code={e.ApplicationExitCode}");
        base.OnExit(e);
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
