namespace BackupSaves.Core.Models;

public sealed class UiSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Remote version the user chose to ignore (e.g. 1.0.6).</summary>
    public string? SkippedUpdateVersion { get; set; }

    /// <summary>Deferred update: apply on real exit, do not restart.</summary>
    public string? PendingUpdateVersion { get; set; }

    /// <summary>Local path to already downloaded zip for pending update.</summary>
    public string? PendingUpdateZipPath { get; set; }
}
