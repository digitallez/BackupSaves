namespace BackupSaves.Core.Models;

public sealed class BackupProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string BackupRoot { get; set; } = "";
    /// <summary>Default for real use is SevenZip; Zip is a fallback.</summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.SevenZip;
    public int RetentionCount { get; set; } = 10;
    /// <summary>If true, skip creating an archive when all source file SHA-256 match the last snapshot.</summary>
    public bool SkipUnchangedByChecksum { get; set; }

    /// <summary>Only run backups while a matching process is running (+ one backup after it exits).</summary>
    public bool WatchProcessEnabled { get; set; }

    /// <summary>Exe name, full path, or wildcard mask (e.g. game.exe, *elden*, C:\Games\*\game.exe).</summary>
    public string? WatchProcessPattern { get; set; }

    /// <summary>Persisted: matching process was observed running on a previous check.</summary>
    public bool WatchProcessWasRunning { get; set; }

    public List<SourceEntry> Sources { get; set; } = [];
    public ScheduleConfig Schedule { get; set; } = new();
}
