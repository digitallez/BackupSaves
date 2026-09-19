namespace BackupSaves.Core.Models;

public sealed class RunHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    /// <summary>True when an archive was created (or other successful action).</summary>
    public bool Success { get; set; }
    /// <summary>True when nothing was saved (unchanged / process not running). Not OK, not FAIL.</summary>
    public bool Skipped { get; set; }
    public string? Message { get; set; }
    public string? ArchivePath { get; set; }
    public RunTrigger Trigger { get; set; }
}
