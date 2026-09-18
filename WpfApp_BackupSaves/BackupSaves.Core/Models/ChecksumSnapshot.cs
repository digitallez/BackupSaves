namespace BackupSaves.Core.Models;

public sealed class ChecksumSnapshot
{
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<FileChecksumEntry> Files { get; set; } = [];
}

public sealed class FileChecksumEntry
{
    /// <summary>Normalized absolute source path.</summary>
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Length { get; set; }
}
