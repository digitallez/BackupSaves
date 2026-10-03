using System.IO;
using System.Windows;
using BackupSaves.Core.Models;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class CloseChoiceWindow : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    /// <summary>True when the user asked to remember tray/exit choice.</summary>
    public bool RememberChoice => RememberCheck.IsChecked == true;

    /// <summary>Path to local debug update archive when the option is shown; otherwise null.</summary>
    public string? LocalUpdateArchivePath => LocalUpdate?.ArchivePath;

    /// <summary>Debug local update candidate when the option is shown.</summary>
    public LocalUpdateCandidate? LocalUpdate { get; private set; }

    /// <summary>True when an update-related option is visible in this dialog.</summary>
    public bool HasUpdateOption { get; private set; }

    private readonly AppSettings? _app;

    public CloseChoiceWindow(AppSettings? app = null)
    {
        _app = app;
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        TryShowLocalUpdateOption();
    }

    /// <summary>Whether the close dialog should be forced (ignore remembered preference).</summary>
    public static bool ShouldForceShowDialog(AppSettings? app = null)
    {
#if DEBUG
        var candidate = UpdateInstaller.FindLocalUpdateCandidate(app?.Ui.DevBuildFolder);
        if (candidate is { IsNewer: true })
            return true;
#endif
        if (app?.Ui.PendingUpdateZipPath is { Length: > 0 } zip && File.Exists(zip))
            return true;
        return false;
    }

    private void TryShowLocalUpdateOption()
    {
#if DEBUG
        LocalUpdate = UpdateInstaller.FindLocalUpdateCandidate(_app?.Ui.DevBuildFolder);
        if (LocalUpdate is not { IsNewer: true })
        {
            LocalUpdate = null;
            return;
        }

        var label = !string.IsNullOrWhiteSpace(LocalUpdate.VersionLabel)
            ? LocalizationService.Text("close.updateFromDev", LocalUpdate.VersionLabel)
            : LocalizationService.Text("close.updateFromArchive", Path.GetFileName(LocalUpdate.DisplayPath));

        UpdateFromArchiveButton.Content = label;
        UpdateFromArchiveButton.ToolTip = LocalizationService.Text("update.localNowTip");
        UpdateFromArchiveButton.Visibility = Visibility.Visible;
        HasUpdateOption = true;
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
