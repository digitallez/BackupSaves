using System.Windows;
using BackupSaves.Core.Models;

namespace WpfApp_BackupSaves.Services;

public static class ThemeManager
{
    private const string BrushDictMarker = "Themes/DarkTheme.xaml";
    private const string LightDictMarker = "Themes/LightTheme.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var app = System.Windows.Application.Current;
        if (app is null)
            return;

        var uri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        var dict = new ResourceDictionary { Source = uri };

        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = app.Resources.MergedDictionaries[i].Source?.OriginalString ?? "";
            if (src.Contains(BrushDictMarker, StringComparison.OrdinalIgnoreCase)
                || src.Contains(LightDictMarker, StringComparison.OrdinalIgnoreCase))
            {
                app.Resources.MergedDictionaries.RemoveAt(i);
            }
        }

        // brushes first so ControlStyles DynamicResource resolve correctly
        app.Resources.MergedDictionaries.Insert(0, dict);
    }

    public static AppTheme Toggle()
    {
        var next = Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Apply(next);
        return next;
    }
}
