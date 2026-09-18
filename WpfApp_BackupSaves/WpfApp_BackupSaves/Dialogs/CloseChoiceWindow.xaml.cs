using System.Windows;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class CloseChoiceWindow : Window
{
    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public CloseChoiceWindow()
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
