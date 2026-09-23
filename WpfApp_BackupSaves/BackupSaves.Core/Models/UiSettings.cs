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

    /// <summary>Last main-window Left (DIP). Null = no saved placement.</summary>
    public double? WindowLeft { get; set; }

    /// <summary>Last main-window Top (DIP).</summary>
    public double? WindowTop { get; set; }

    /// <summary>Last main-window Width (DIP, restore bounds when maximized).</summary>
    public double? WindowWidth { get; set; }

    /// <summary>Last main-window Height (DIP, restore bounds when maximized).</summary>
    public double? WindowHeight { get; set; }

    /// <summary>Whether the main window was maximized.</summary>
    public bool WindowMaximized { get; set; }
}
