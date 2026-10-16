namespace BackupSaves.Core.Models;

/// <summary>Restore progress: <paramref name="Done"/> of <paramref name="Total"/> entries processed.</summary>
public readonly record struct RestoreProgress(int Done, int Total, string? CurrentPath = null);
