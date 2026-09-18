using System.Windows.Threading;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// While the GUI is open, runs backups for profiles with Task Scheduler off and InAppEnabled,
/// using each profile's IntervalMinutes.
/// </summary>
public sealed class InAppBackupScheduler : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<AppSettings> _getApp;
    private readonly Func<Guid, Task> _runBackup;
    private readonly Action<string> _setStatus;
    private readonly EventHandler _tickHandler;
    private bool _tickRunning;
    private bool _disposed;

    public InAppBackupScheduler(
        Func<AppSettings> getApp,
        Func<Guid, Task> runBackup,
        Action<string> setStatus)
    {
        _getApp = getApp;
        _runBackup = runBackup;
        _setStatus = setStatus;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _tickHandler = (_, _) => _ = OnTickAsync();
        _timer.Tick += _tickHandler;
    }

    public void Start()
    {
        if (_disposed) return;
        if (!_timer.IsEnabled)
        {
            AppLog.Default.Info("InAppSchedule", "Timer started (30s tick)");
            _timer.Start();
        }

        _ = OnTickAsync();
    }

    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            AppLog.Default.Info("InAppSchedule", "Timer stopped");
        }
    }

    private async Task OnTickAsync()
    {
        if (_disposed || _tickRunning)
            return;

        _tickRunning = true;
        try
        {
            var app = _getApp();
            var due = app.Profiles
                .Where(IsDue)
                .OrderBy(p => p.Schedule.LastInAppBackupUtc ?? DateTimeOffset.MinValue)
                .ToList();

            foreach (var profile in due)
            {
                if (_disposed) break;

                var mins = Math.Max(1, profile.Schedule.IntervalMinutes ?? 60);
                AppLog.Default.Info("InAppSchedule",
                    $"Due: «{profile.Name}» interval={mins}m last={profile.Schedule.LastInAppBackupUtc:o}");
                _setStatus($"Автобэкап «{profile.Name}»…");

                try
                {
                    await _runBackup(profile.Id);
                }
                catch (Exception ex)
                {
                    AppLog.Default.Error("InAppSchedule", $"Failed «{profile.Name}»", ex);
                }

                var fresh = _getApp().Profiles.FirstOrDefault(p => p.Id == profile.Id);
                if (fresh is not null)
                    fresh.Schedule.LastInAppBackupUtc = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _tickRunning = false;
        }
    }

    private static bool IsDue(BackupProfile profile)
    {
        var s = profile.Schedule;
        if (s.Enabled || !s.InAppEnabled)
            return false;

        var mins = s.IntervalMinutes ?? 0;
        if (mins < 1)
            return false;

        if (s.LastInAppBackupUtc is null)
            return true;

        return DateTimeOffset.UtcNow - s.LastInAppBackupUtc.Value >= TimeSpan.FromMinutes(mins);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= _tickHandler;
    }
}
