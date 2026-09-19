using BackupSaves.Core.Models;
using BackupSaves.Core.Results;

namespace BackupSaves.Core.Services;

public interface IBackupRunner
{
    Task<BackupResult> RunProfileAsync(Guid profileId, RunTrigger trigger, CancellationToken ct = default);
}

/// <summary>Loads settings, runs backup, appends history. Used by UI and headless CLI.</summary>
public sealed class BackupRunner : IBackupRunner
{
    private readonly ISettingsStore _settings;
    private readonly IBackupService _backup;
    private readonly IAppLog _log;

    public BackupRunner(ISettingsStore? settings = null, IBackupService? backup = null, IAppLog? log = null)
    {
        _settings = settings ?? new SettingsStore();
        _backup = backup ?? new BackupService();
        _log = log ?? AppLog.Default;
    }

    public async Task<BackupResult> RunProfileAsync(Guid profileId, RunTrigger trigger, CancellationToken ct = default)
    {
        var app = await _settings.LoadAsync(ct);
        var profile = app.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            _log.Error("Backup", $"Профиль не найден: {profileId} (trigger={trigger})");
            return BackupResult.Fail($"Профиль не найден: {profileId}");
        }

        _log.Info("Backup",
            $"Старт: profile=\"{profile.Name}\" id={profile.Id:N} format={profile.Format} trigger={trigger} sources={profile.Sources.Count}");

        var started = DateTimeOffset.UtcNow;
        BackupResult result;

        try
        {
            if (ShouldSkipForProcessWatch(profile, out var skipMessage, out var farewell))
            {
                _log.Info("Backup",
                    $"Пропуск по процессу «{profile.Name}»: {skipMessage}");
                result = BackupResult.SkippedReason(skipMessage!);
            }
            else
            {
                if (farewell)
                    _log.Info("Backup", $"Прощальный бэкап «{profile.Name}»: процесс только что завершился");

                result = await _backup.BackupAsync(profile, ct);
            }

            if (profile.WatchProcessEnabled && ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern))
            {
                profile.WatchProcessWasRunning =
                    ProcessWatchService.IsAnyMatchingProcessRunning(profile.WatchProcessPattern);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Backup", $"Исключение при бэкапе «{profile.Name}»", ex);
            result = BackupResult.Fail(ex.Message);
        }

        if (result.Success)
        {
            if (result.Skipped)
            {
                _log.Info("Backup",
                    $"Пропуск: «{profile.Name}» — {result.StatusMessage}");
            }
            else
            {
                _log.Info("Backup",
                    $"Успех: «{profile.Name}» files={result.FilesArchived} archive=\"{result.ArchivePath}\"");
            }
        }
        else
        {
            _log.Error("Backup", $"Ошибка: «{profile.Name}» — {result.ErrorMessage}");
        }

        app.History.Insert(0, new RunHistoryEntry
        {
            ProfileId = profileId,
            ProfileName = profile.Name,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Success = result.Success,
            Message = result.Skipped
                ? result.StatusMessage
                : result.Success
                    ? $"Файлов: {result.FilesArchived} • есть изменения • архив создан"
                    : result.ErrorMessage,
            ArchivePath = result.ArchivePath,
            Trigger = trigger
        });

        if (app.History.Count > 200)
            app.History = app.History.Take(200).ToList();

        await _settings.SaveAsync(app, ct);
        _log.Info("Settings", "История запусков обновлена после бэкапа");
        return result;
    }

    /// <summary>
    /// Returns true if backup must be skipped because watch is on and process is not running
    /// (and this is not the farewell backup after exit).
    /// </summary>
    public static bool ShouldSkipForProcessWatch(BackupProfile profile, out string? message, out bool farewell)
    {
        message = null;
        farewell = false;

        if (!profile.WatchProcessEnabled || !ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern))
            return false;

        var running = ProcessWatchService.IsAnyMatchingProcessRunning(profile.WatchProcessPattern);
        if (running)
            return false;

        if (profile.WatchProcessWasRunning)
        {
            farewell = true;
            return false;
        }

        message = $"Процесс «{profile.WatchProcessPattern}» не запущен — бэкап пропущен";
        return true;
    }
}
