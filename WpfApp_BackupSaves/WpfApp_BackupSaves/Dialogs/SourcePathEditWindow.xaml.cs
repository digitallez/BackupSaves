using System.Windows;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using WpfApp_BackupSaves.Dialogs;

namespace WpfApp_BackupSaves.Dialogs;

public partial class SourcePathEditWindow : Window
{
    public string PathText { get; private set; }

    public SourcePathEditWindow(string initialPath)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        PathText = initialPath ?? "";
        PathBox.Text = PathText;
        Loaded += (_, _) =>
        {
            PathBox.Focus();
            PathBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        PathText = PathBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(PathText))
        {
            AppMessageBox.Show(this, LocalizationService.Text("profile.errSourcePathEmpty"),
                LocalizationService.Text("profile.editPathTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
