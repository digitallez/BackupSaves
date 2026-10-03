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

/// <summary>What happens when the user closes the main window.</summary>
public enum CloseActionPreference
{
    /// <summary>Show the close-choice dialog every time.</summary>
    Ask = 0,
    HideToTray = 1,
    Exit = 2
}
