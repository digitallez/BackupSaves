using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupSaves.Core.Models;

public sealed class ScheduleConfig
{
    /// <summary>Unknown JSON fields kept across load/save so older builds do not wipe newer settings.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Windows Task Scheduler registration.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// When Task Scheduler is off: run backups from the open GUI on <see cref="IntervalMinutes"/>.
    /// </summary>
    public bool InAppEnabled { get; set; }

    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
    public TimeSpan? TimeOfDay { get; set; } = new TimeSpan(2, 0, 0);
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public int? IntervalMinutes { get; set; }

    /// <summary>Last in-app interval backup attempt (UTC).</summary>
    public DateTimeOffset? LastInAppBackupUtc { get; set; }
}
