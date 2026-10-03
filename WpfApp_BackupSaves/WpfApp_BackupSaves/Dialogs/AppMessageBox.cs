using System.Windows;

namespace WpfApp_BackupSaves.Dialogs;

/// <summary>
/// Themed in-app message box (same chrome/brushes as the main window).
/// </summary>
public static class AppMessageBox
{
    public static MessageBoxResult Show(
        Window? owner,
        string? message,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var dlg = new AppMessageBoxWindow(message ?? "", caption, button, icon)
        {
            Owner = owner ?? System.Windows.Application.Current?.MainWindow
        };
        dlg.ShowDialog();
        return dlg.Result;
    }
}
