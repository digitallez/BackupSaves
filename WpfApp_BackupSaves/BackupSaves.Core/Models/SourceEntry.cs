namespace BackupSaves.Core.Models;

public sealed class SourceEntry
{
    public string Path { get; set; } = "";
    public SourceType Type { get; set; } = SourceType.Directory;
}
