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
    Scheduler
}
