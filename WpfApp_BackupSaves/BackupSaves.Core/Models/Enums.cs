namespace BackupSaves.Core.Models;

public enum ArchiveFormat
{
    Zip,
    SevenZip
}

public enum SourceType
{
    File,
    Directory
}

public enum ScheduleKind
{
    Daily,
    Weekly,
    OnLogon,
    Interval
}

public enum RunTrigger
{
    Manual,
    Scheduler,
    /// <summary>Periodic backup while the GUI app is running (Task Scheduler off).</summary>
    InApp
}
