namespace BackupSaves.Core.Models;

public sealed class UiSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Dark;
}
