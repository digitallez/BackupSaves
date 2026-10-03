using System.Windows;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class FirstRunInstallWindow : Window
{
    public bool InstallChosen { get; private set; }

    public FirstRunInstallWindow()
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        PathHintBlock.Text = LocalizationService.Text(
            "install.firstHint",
            AppInstallService.RecommendedInstallDirectory,
            AppContext.BaseDirectory.TrimEnd('\\', '/'));
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallChosen = true;
        DialogResult = true;
        Close();
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e)
    {
        InstallChosen = false;
        DialogResult = true;
        Close();
    }
}
