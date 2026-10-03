using System.IO;
using System.Windows;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class LocalArchiveUpdateWindow : Window
{
    public bool Accepted { get; private set; }

    public LocalArchiveUpdateWindow(string archivePath, bool archiveLikelyNewer)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);

        var name = Path.GetFileName(archivePath);
        TitleBlock.Text = LocalizationService.Text("update.localAvailable", name);
        BodyBlock.Text = LocalizationService.Text(
            "update.localBody",
            AppVersion.Current,
            archivePath);

        if (!archiveLikelyNewer)
        {
            WarnBlock.Text = LocalizationService.Text("update.localOlderWarn");
            WarnBlock.Visibility = Visibility.Visible;
        }
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
