using System.IO;
using System.Windows;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class CloseChoiceWindow : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    /// <summary>Path to local debug update archive when the option is shown; otherwise null.</summary>
    public string? LocalUpdateArchivePath { get; private set; }

    public CloseChoiceWindow()
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        TryShowLocalUpdateOption();
    }

    private void TryShowLocalUpdateOption()
    {
#if DEBUG
        LocalUpdateArchivePath = UpdateInstaller.FindLocalBuildFolderArchive();
        if (string.IsNullOrWhiteSpace(LocalUpdateArchivePath))
            return;

        var name = Path.GetFileName(LocalUpdateArchivePath);
        var label = LocalizationService.Text("close.updateFromArchive", name);
        if (!UpdateInstaller.IsLocalArchiveLikelyNewer(LocalUpdateArchivePath))
            label += " ⚠";

        UpdateFromArchiveButton.Content = label;
        UpdateFromArchiveButton.ToolTip = UpdateInstaller.IsLocalArchiveLikelyNewer(LocalUpdateArchivePath)
            ? LocalizationService.Text("update.localNowTip")
            : LocalizationService.Text("update.localOlderWarn");
        UpdateFromArchiveButton.Visibility = Visibility.Visible;
#endif
    }

    private void Tray_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.HideToTray;
        DialogResult = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Exit;
        DialogResult = true;
        Close();
    }

    private void UpdateFromArchive_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.UpdateFromLocalArchive;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
