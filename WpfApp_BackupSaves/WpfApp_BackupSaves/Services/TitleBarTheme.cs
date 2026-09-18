using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BackupSaves.Core.Models;

namespace WpfApp_BackupSaves.Services;

/// <summary>
/// Keeps Win10/11 title bar dark in dark theme (including inactive windows).
/// Default OS chrome goes light/white when the window loses focus.
/// </summary>
public static class TitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // Dark theme (matches DarkTheme.xaml)
    private static readonly uint DarkCaption = Rgb(0x1E, 0x1E, 0x1E);
    private static readonly uint DarkText = Rgb(0xE8, 0xE8, 0xE8);
    private static readonly uint DarkBorder = Rgb(0x3F, 0x3F, 0x46);

    // Light theme
    private static readonly uint LightCaption = Rgb(0xFA, 0xFA, 0xFA);
    private static readonly uint LightText = Rgb(0x1A, 0x1A, 0x1A);
    private static readonly uint LightBorder = Rgb(0xDD, 0xDD, 0xDD);

    public static void Attach(Window window)
    {
        void ApplyHandler(object? s, EventArgs e) => Apply(window);

        window.SourceInitialized += ApplyHandler;
        window.Activated += ApplyHandler;
        window.Deactivated += ApplyHandler;

        if (window.IsInitialized)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                Apply(window);
        }
    }

    public static void ApplyAllOpenWindows()
    {
        foreach (Window w in System.Windows.Application.Current.Windows)
            Apply(w);
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
            return;

        var dark = ThemeManager.Current == AppTheme.Dark;
        var useDark = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));

        // Win11 22000+: force caption/text colors so inactive title bar stays themed
        var caption = dark ? DarkCaption : LightCaption;
        var text = dark ? DarkText : LightText;
        var border = dark ? DarkBorder : LightBorder;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(uint));
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(uint));
    }

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref uint attrValue,
        int attrSize);
}
