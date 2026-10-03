using BackupSaves.Core.Models;
using Microsoft.Win32.TaskScheduler;

namespace BackupSaves.Scheduler.Services;

public interface IWindowsTaskSchedulerService
{
    string GetTaskName(BackupProfile profile);
    void Upsert(BackupProfile profile, string exePath);
    void Delete(BackupProfile profile);
    bool Exists(BackupProfile profile);
    /// <summary>Next planned run from Task Scheduler (local), or null if unknown/disabled.</summary>
    DateTimeOffset? GetNextRunTime(BackupProfile profile);
}

public sealed class WindowsTaskSchedulerService : IWindowsTaskSchedulerService
{
    private const string FolderName = "BackupSaves";

    public string GetTaskName(BackupProfile profile) =>
        $@"\{FolderName}\Backup_{profile.Slug}_{profile.Id:N}";

    public bool Exists(BackupProfile profile)
    {
        using var ts = new TaskService();
        return ts.GetTask(GetTaskName(profile)) is not null;
    }

    public DateTimeOffset? GetNextRunTime(BackupProfile profile)
    {
        if (!profile.Schedule.Enabled)
            return null;

        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(GetTaskName(profile));
            if (task is null)
                return null;

            var next = task.NextRunTime;
            if (next <= DateTime.MinValue.AddYears(1) || next.Year < 2000)
                return null;

            return new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Local));
        }
        catch
        {
            return null;
        }
    }

    public void Delete(BackupProfile profile)
    {
        using var ts = new TaskService();
        try
        {
            EnsureFolder(ts).DeleteTask($"Backup_{profile.Slug}_{profile.Id:N}");
        }
        catch
        {
            // ignore missing
        }
    }

    public void Upsert(BackupProfile profile, string exePath)
    {
        if (!profile.Schedule.Enabled)
        {
            Delete(profile);
            return;
        }

        using var ts = new TaskService();
        var folder = EnsureFolder(ts);
        var taskName = $"Backup_{profile.Slug}_{profile.Id:N}";

        var td = ts.NewTask();
        td.RegistrationInfo.Description = $"BackupSaves: {profile.Name}";
        td.Settings.AllowDemandStart = true;
        td.Settings.StartWhenAvailable = true;
        td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Principal.RunLevel = TaskRunLevel.LUA;
        td.Principal.LogonType = TaskLogonType.InteractiveToken;

        td.Actions.Clear();
        td.Actions.Add(new ExecAction(exePath, $"--backup {profile.Id}", Path.GetDirectoryName(exePath)));

        td.Triggers.Clear();
        switch (profile.Schedule.Kind)
        {
            case ScheduleKind.Daily:
            {
                var t = new DailyTrigger
                {
                    StartBoundary = NextLocal(profile.Schedule.TimeOfDay ?? TimeSpan.FromHours(2))
                };
                td.Triggers.Add(t);
                break;
            }
            case ScheduleKind.Weekly:
            {
                var days = profile.Schedule.DaysOfWeek.Count > 0
                    ? ToDaysOfTheWeek(profile.Schedule.DaysOfWeek)
                    : DaysOfTheWeek.Monday;
                var t = new WeeklyTrigger
                {
                    DaysOfWeek = days,
                    StartBoundary = NextLocal(profile.Schedule.TimeOfDay ?? TimeSpan.FromHours(2))
                };
                td.Triggers.Add(t);
                break;
            }
            case ScheduleKind.OnLogon:
                td.Triggers.Add(new LogonTrigger());
                break;
            case ScheduleKind.Interval:
            {
                var minutes = Math.Max(1, profile.Schedule.IntervalMinutes ?? 60);
                var t = new TimeTrigger
                {
                    StartBoundary = DateTime.Now.AddMinutes(1),
                    Repetition = new RepetitionPattern(TimeSpan.FromMinutes(minutes), TimeSpan.Zero)
                };
                td.Triggers.Add(t);
                break;
            }
        }

        folder.RegisterTaskDefinition(taskName, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
    }

    private static TaskFolder EnsureFolder(TaskService ts)
    {
        try
        {
            return ts.RootFolder.CreateFolder(FolderName);
        }
        catch
        {
            return ts.RootFolder.SubFolders[FolderName];
        }
    }

    private static DateTime NextLocal(TimeSpan timeOfDay)
    {
        var today = DateTime.Today.Add(timeOfDay);
        return today > DateTime.Now ? today : today.AddDays(1);
    }

    private static DaysOfTheWeek ToDaysOfTheWeek(IEnumerable<DayOfWeek> days)
    {
        DaysOfTheWeek result = 0;
        foreach (var d in days)
        {
            result |= d switch
            {
                DayOfWeek.Sunday => DaysOfTheWeek.Sunday,
                DayOfWeek.Monday => DaysOfTheWeek.Monday,
                DayOfWeek.Tuesday => DaysOfTheWeek.Tuesday,
                DayOfWeek.Wednesday => DaysOfTheWeek.Wednesday,
                DayOfWeek.Thursday => DaysOfTheWeek.Thursday,
                DayOfWeek.Friday => DaysOfTheWeek.Friday,
                DayOfWeek.Saturday => DaysOfTheWeek.Saturday,
                _ => 0
            };
        }

        return result == 0 ? DaysOfTheWeek.Monday : result;
    }
}
