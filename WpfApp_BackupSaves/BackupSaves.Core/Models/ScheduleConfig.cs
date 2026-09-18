namespace BackupSaves.Core.Models;

public sealed class ScheduleConfig
{
    public bool Enabled { get; set; }
    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;
    public TimeSpan? TimeOfDay { get; set; } = new TimeSpan(2, 0, 0);
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public int? IntervalMinutes { get; set; }
}
