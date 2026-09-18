namespace BackupSaves.Core.Results;

public sealed class BackupResult
{
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string? ArchivePath { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StatusMessage { get; init; }
    public int FilesArchived { get; init; }

    public static BackupResult Ok(string archivePath, int files) => new()
    {
        Success = true,
        ArchivePath = archivePath,
        FilesArchived = files
    };

    public static BackupResult SkippedUnchanged(int fileCount) => new()
    {
        Success = true,
        Skipped = true,
        FilesArchived = 0,
        StatusMessage = $"Изменений нет ({fileCount} файл(ов)) — архив не создан"
    };

    public static BackupResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}

public sealed class RestoreFileError
{
    public string SourcePath { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class RestoreResult
{
    public bool Success { get; init; }
    public int RestoredCount { get; init; }
    public List<RestoreFileError> Errors { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static RestoreResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };
}
