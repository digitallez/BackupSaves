using System.Windows;
using WpfApp_BackupSaves.Services;

namespace WpfApp_BackupSaves.Dialogs;

public partial class UpdateAvailableWindow : Window
{
    public UpdateChoice Choice { get; private set; } = UpdateChoice.LaterAskAgain;

    public UpdateAvailableWindow(string currentVersion, string newVersion, string? notes)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        TitleBlock.Text = $"Доступна версия {newVersion}";
        var body = $"Сейчас установлена: {currentVersion}\n\nПриложение скачает пакет с GitHub Releases и установит обновление.";
        if (!string.IsNullOrWhiteSpace(notes))
        {
            var trimmed = notes.Trim();
            if (trimmed.Length > 400)
                trimmed = trimmed[..400] + "…";
            body += "\n\n" + trimmed;
        }

        BodyBlock.Text = body;
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.UpdateNow;
        DialogResult = true;
        Close();
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.UpdateOnClose;
        DialogResult = true;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.SkipThisVersion;
        DialogResult = true;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.LaterAskAgain;
        DialogResult = false;
        Close();
    }
}
