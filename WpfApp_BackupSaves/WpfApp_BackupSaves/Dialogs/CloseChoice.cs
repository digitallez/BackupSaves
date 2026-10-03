namespace WpfApp_BackupSaves.Dialogs;

public enum CloseChoice
{
    Cancel,
    HideToTray,
    Exit,
    /// <summary>Debug only: apply local build-folder archive and restart.</summary>
    UpdateFromLocalArchive
}
