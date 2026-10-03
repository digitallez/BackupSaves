using System.Windows;
using System.Windows.Shell;

namespace WpfApp_BackupSaves.Services;

/// <summary>Replaces system title bar (Win10 inactive = white) with client-drawn chrome.</summary>
public static class CustomWindowChrome
{
    public const double CaptionHeight = 32;

    public static void Apply(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = window.ResizeMode == ResizeMode.NoResize
            ? ResizeMode.NoResize
            : ResizeMode.CanResize;

        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
    }
}
