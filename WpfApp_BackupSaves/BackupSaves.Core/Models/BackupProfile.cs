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
    public List<SourceEntry> Sources { get; set; } = [];
    public ScheduleConfig Schedule { get; set; } = new();
}
