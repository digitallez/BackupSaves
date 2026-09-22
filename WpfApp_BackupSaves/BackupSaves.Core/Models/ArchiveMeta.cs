namespace BackupSaves.Core.Models;

public sealed class ArchiveMeta
{
    public string? DisplayName { get; set; }

    /// <summary>
    /// When true, the archive is ignored by retention: not counted toward the limit
    /// and never deleted by automatic cleanup.
    /// </summary>
    public bool ExcludeFromRetention { get; set; }
}
