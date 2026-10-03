using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BackupSaves.Core.Services;
using WpfApp_BackupSaves.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace WpfApp_BackupSaves.Dialogs;

public partial class AppMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public AppMessageBoxWindow(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        InitializeComponent();
        CustomWindowChrome.Apply(this);
        Title = string.IsNullOrWhiteSpace(caption)
            ? LocalizationService.Text("common.appName")
            : caption;
        MessageBlock.Text = message ?? "";
        ApplyIcon(image);
        BuildButtons(buttons);
    }

    private void ApplyIcon(MessageBoxImage image)
    {
        string? glyph = image switch
        {
            MessageBoxImage.Error => "\uEA39",       // ErrorBadge
            MessageBoxImage.Warning => "\uE7BA",     // Warning
            MessageBoxImage.Information => "\uE946", // Info
            MessageBoxImage.Question => "\uE9CE",    // StatusCircleQuestionMark
            _ => null
        };

        if (glyph is null)
        {
            IconBlock.Visibility = Visibility.Collapsed;
            return;
        }

        IconBlock.Text = glyph;
        IconBlock.Foreground = image switch
        {
            MessageBoxImage.Error => BrushOr("FailBrush", MediaColor.FromRgb(0xCD, 0x5C, 0x5C)),
            MessageBoxImage.Warning => BrushOr("ArchiveAgeWarmBrush", MediaColor.FromRgb(0xDA, 0xA5, 0x20)),
            MessageBoxImage.Information => BrushOr("ArchiveAgeCoolBrush", MediaColor.FromRgb(0x1E, 0x90, 0xFF)),
            MessageBoxImage.Question => BrushOr("SecondaryTextBrush", MediaColor.FromRgb(0x80, 0x80, 0x80)),
            _ => BrushOr("PrimaryTextBrush", MediaColor.FromRgb(0x80, 0x80, 0x80))
        };
        IconBlock.Visibility = Visibility.Visible;
    }

    private static MediaBrush BrushOr(string key, MediaColor fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush b)
            return b;
        var brush = new SolidColorBrush(fallback);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        ButtonsPanel.Children.Clear();

        void Add(string locKey, MessageBoxResult result, bool isDefault, bool isCancel)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = LocalizationService.Text(locKey),
                MinWidth = 96,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
                Tag = result
            };
            btn.Click += (_, _) =>
            {
                Result = result;
                DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
                Close();
            };
            ButtonsPanel.Children.Add(btn);
        }

        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                Add("common.ok", MessageBoxResult.OK, isDefault: true, isCancel: false);
                Add("common.cancel", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                Add("common.yes", MessageBoxResult.Yes, isDefault: true, isCancel: false);
                Add("common.no", MessageBoxResult.No, isDefault: false, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                Add("common.yes", MessageBoxResult.Yes, isDefault: true, isCancel: false);
                Add("common.no", MessageBoxResult.No, isDefault: false, isCancel: false);
                Add("common.cancel", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                break;
            default:
                Add("common.ok", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }
    }
}
