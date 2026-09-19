using System.Windows.Threading;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// While the GUI is open, runs backups for profiles with Task Scheduler off and InAppEnabled,
/// using each profile's IntervalMinutes. Also triggers a farewell backup when a watched process exits.
/// </summary>
public sealed class InAppBackupScheduler : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<AppSettings> _getApp;
    private readonly Func<Guid, Task> _runBackup;
    private readonly Func<Task> _saveApp;
    private readonly Action<string> _setStatus;
    private readonly EventHandler _tickHandler;
    private bool _tickRunning;
    private bool _disposed;

    public InAppBackupScheduler(
        Func<AppSettings> getApp,
        Func<Guid, Task> runBackup,
        Func<Task> saveApp,
        Action<string> setStatus)
    {
        _getApp = getApp;
        _runBackup = runBackup;
        _saveApp = saveApp;
        _setStatus = setStatus;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _tickHandler = (_, _) => _ = OnTickAsync();
        _timer.Tick += _tickHandler;
    }

    public void Start()
    {
        if (_disposed) return;
        if (!_timer.IsEnabled)
        {
            AppLog.Default.Info("InAppSchedule", "Timer started (15s tick)");
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
            var runningMap = await Task.Run(() => BuildRunningMap(app)).ConfigureAwait(true);

            if (ObserveWatchProcesses(app, runningMap))
                await _saveApp();

            var due = app.Profiles
                .Where(p => IsDue(p, runningMap))
                .OrderBy(p => p.Schedule.LastInAppBackupUtc ?? DateTimeOffset.MinValue)
                .ToList();

            foreach (var profile in due)
            {
                if (_disposed) break;

                var mins = Math.Max(1, profile.Schedule.IntervalMinutes ?? 60);
                var farewell = IsFarewellDue(profile, runningMap);
                AppLog.Default.Info("InAppSchedule",
                    $"Due: «{profile.Name}» interval={mins}m farewell={farewell} last={profile.Schedule.LastInAppBackupUtc:o}");
                _setStatus(farewell
                    ? LocalizationService.Text("status.farewellBackup", profile.Name)
                    : LocalizationService.Text("status.autoBackupRunning", profile.Name));

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

    /// <summary>
    /// One batched process evaluation per tick (PID cache + shared snapshot for wildcards).
    /// Uses each profile's WatchProcessScanSeconds as the full-scan interval.
    /// </summary>
    private static IReadOnlyDictionary<string, bool> BuildRunningMap(AppSettings app)
    {
        var shortest = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in app.Profiles)
        {
            if (!profile.WatchProcessEnabled
                || !ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern))
                continue;

            var key = profile.WatchProcessPattern!.Trim();
            var interval = TimeSpan.FromSeconds(
                Math.Max(1, profile.WatchProcessScanSeconds <= 0 ? 10 : profile.WatchProcessScanSeconds));

            if (!shortest.TryGetValue(key, out var existing) || interval < existing)
                shortest[key] = interval;
        }

        if (shortest.Count == 0)
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        return ProcessWatchService.EvaluatePatterns(
            shortest.Select(kv => (kv.Key, (TimeSpan?)kv.Value)));
    }

    private static bool LookupRunning(IReadOnlyDictionary<string, bool> map, string? pattern)
    {
        if (!ProcessWatchService.IsMatchPatternConfigured(pattern))
            return false;

        return map.TryGetValue(pattern!.Trim(), out var running) && running;
    }

    /// <summary>Mark watched processes as running when first observed (needed for farewell backup).</summary>
    private static bool ObserveWatchProcesses(AppSettings app, IReadOnlyDictionary<string, bool> runningMap)
    {
        var changed = false;
        foreach (var profile in app.Profiles)
        {
            if (!profile.WatchProcessEnabled
                || !ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern))
                continue;

            var running = LookupRunning(runningMap, profile.WatchProcessPattern);
            if (running && !profile.WatchProcessWasRunning)
            {
                profile.WatchProcessWasRunning = true;
                changed = true;
                AppLog.Default.Info("InAppSchedule",
                    $"Process «{profile.WatchProcessPattern}» started (profile «{profile.Name}»)");
            }
        }

        return changed;
    }

    private static bool IsFarewellDue(BackupProfile profile, IReadOnlyDictionary<string, bool> runningMap)
    {
        if (!profile.WatchProcessEnabled
            || !ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern))
            return false;

        if (!profile.WatchProcessWasRunning)
            return false;

        return !LookupRunning(runningMap, profile.WatchProcessPattern);
    }

    private static bool IsDue(BackupProfile profile, IReadOnlyDictionary<string, bool> runningMap)
    {
        var s = profile.Schedule;
        if (s.Enabled || !s.InAppEnabled)
            return false;

        var mins = s.IntervalMinutes ?? 0;
        if (mins < 1)
            return false;

        // Watched process just exited → backup once ASAP (ignore interval).
        if (IsFarewellDue(profile, runningMap))
            return true;

        // Watching enabled but process not running → no periodic backup.
        if (profile.WatchProcessEnabled
            && ProcessWatchService.IsMatchPatternConfigured(profile.WatchProcessPattern)
            && !LookupRunning(runningMap, profile.WatchProcessPattern))
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
