namespace BackupSaves.Core.Models;

public sealed class Manifest
{
    public int Version { get; set; } = 1;
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public ArchiveFormat Format { get; set; }
    public List<ManifestEntry> Entries { get; set; } = [];
}

public sealed class ManifestEntry
{
    public string SourcePath { get; set; } = "";
    public string SourcePathTemplate { get; set; } = "";
    public string ArchivePath { get; set; } = "";
    public SourceType Type { get; set; } = SourceType.File;
}
