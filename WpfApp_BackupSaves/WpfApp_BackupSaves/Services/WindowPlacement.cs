using System.Windows;
using BackupSaves.Core.Models;
using WinForms = System.Windows.Forms;

namespace WpfApp_BackupSaves.Services;

/// <summary>Persist / restore main window bounds with multi-monitor visibility checks.</summary>
internal static class WindowPlacement
{
    public const double DefaultWidth = 1110;
    public const double DefaultHeight = 740;

    /// <summary>Minimum overlap (DIP) with any screen working area to treat placement as visible.</summary>
    private const double MinVisibleDip = 48;

    public static bool HasSavedPlacement(UiSettings ui) =>
        ui.WindowLeft is not null
        && ui.WindowTop is not null
        && ui.WindowWidth is > 0
        && ui.WindowHeight is > 0;

    public static void Clear(UiSettings ui)
    {
        ui.WindowLeft = null;
        ui.WindowTop = null;
        ui.WindowWidth = null;
        ui.WindowHeight = null;
        ui.WindowMaximized = false;
    }

    public static void Capture(Window window, UiSettings ui)
    {
        if (window.WindowState == WindowState.Minimized)
            return;

        var bounds = window.WindowState == WindowState.Maximized
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        ui.WindowLeft = bounds.Left;
        ui.WindowTop = bounds.Top;
        ui.WindowWidth = bounds.Width;
        ui.WindowHeight = bounds.Height;
        ui.WindowMaximized = window.WindowState == WindowState.Maximized;
    }

    /// <summary>
    /// Apply saved placement if present and visible; otherwise center default size on the primary work area.
    /// </summary>
    public static void ApplyOrDefault(Window window, UiSettings ui)
    {
        if (TryApply(window, ui))
            return;

        ApplyDefault(window);
    }

    public static bool TryApply(Window window, UiSettings ui)
    {
        if (!HasSavedPlacement(ui))
            return false;

        var left = ui.WindowLeft!.Value;
        var top = ui.WindowTop!.Value;
        var width = ClampSize(ui.WindowWidth!.Value, DefaultWidth);
        var height = ClampSize(ui.WindowHeight!.Value, DefaultHeight);

        if (!IsPlacementVisible(left, top, width, height))
            return false;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;

        if (ui.WindowMaximized)
            window.WindowState = WindowState.Maximized;

        return true;
    }

    public static void ApplyDefault(Window window)
    {
        window.WindowState = WindowState.Normal;
        window.Width = DefaultWidth;
        window.Height = DefaultHeight;

        var work = SystemParameters.WorkArea;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = work.Left + Math.Max(0, (work.Width - DefaultWidth) / 2);
        window.Top = work.Top + Math.Max(0, (work.Height - DefaultHeight) / 2);
    }

    public static bool IsPlacementVisible(double left, double top, double width, double height)
    {
        if (width < MinVisibleDip || height < MinVisibleDip)
            return false;

        var windowRect = new Rect(left, top, width, height);
        foreach (var area in EnumerateWorkingAreasDip())
        {
            var hit = Rect.Intersect(windowRect, area);
            if (!hit.IsEmpty && hit.Width >= MinVisibleDip && hit.Height >= MinVisibleDip)
                return true;
        }

        return false;
    }

    private static double ClampSize(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < MinVisibleDip)
            return fallback;

        var max = Math.Max(fallback, SystemParameters.VirtualScreenWidth);
        return Math.Min(value, max);
    }

    /// <summary>
    /// Screen working areas in WPF DIP. Uses device→DIP via primary presentation scale when available;
    /// falls back to 96 DPI (1:1) before the window has a PresentationSource.
    /// </summary>
    private static IEnumerable<Rect> EnumerateWorkingAreasDip()
    {
        double scaleX = 1, scaleY = 1;
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            scaleX = g.DpiX / 96.0;
            scaleY = g.DpiY / 96.0;
            if (scaleX <= 0) scaleX = 1;
            if (scaleY <= 0) scaleY = 1;
        }
        catch
        {
            // keep 1:1
        }

        foreach (var screen in WinForms.Screen.AllScreens)
        {
            var wa = screen.WorkingArea;
            yield return new Rect(
                wa.Left / scaleX,
                wa.Top / scaleY,
                wa.Width / scaleX,
                wa.Height / scaleY);
        }
    }
}
