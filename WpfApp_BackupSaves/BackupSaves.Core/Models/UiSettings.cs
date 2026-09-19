namespace BackupSaves.Core.Models;

public sealed class UiSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Active locale id from Locales JSON (@id), e.g. "ru", "cn". Null = resolve from OS @match.</summary>
    public string? Language { get; set; }

    /// <summary>Remote version the user chose to ignore (e.g. 1.0.6).</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Deferred update: apply on real exit, do not restart.</summary>
    public string? PendingUpdateVersion { get; set; }

    /// <summary>Local path to already downloaded zip for pending update.</summary>
    public string? PendingUpdateZipPath { get; set; }
}
