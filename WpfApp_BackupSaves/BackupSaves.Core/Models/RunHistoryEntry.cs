namespace BackupSaves.Core.Models;

public sealed class RunHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ArchivePath { get; set; }
    public RunTrigger Trigger { get; set; }
}
