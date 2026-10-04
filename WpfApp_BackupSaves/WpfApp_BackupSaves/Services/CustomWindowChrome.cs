using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace WpfApp_BackupSaves.Services;

/// <summary>Replaces system title bar (Win10 inactive = white) with client-drawn chrome.</summary>
public static class CustomWindowChrome
{
    public const double CaptionHeight = 32;
    private const string OutlineTag = "WindowOutline";

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

        AttachOutline(window);
    }

    /// <summary>1px client border. System chrome is off, so DWM border color never shows.</summary>
    private static void AttachOutline(Window window)
    {
        if (window.Content is Border { Tag: OutlineTag })
            return;

        if (window.Content is not UIElement content)
            return;

        // Content is still the window's logical child; detach before reparenting.
        window.Content = null;
        var outline = new Border
        {
            Tag = OutlineTag,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Child = content
        };
        outline.SetResourceReference(Border.BorderBrushProperty, "WindowBorderBrush");
        window.Content = outline;
    }
}
