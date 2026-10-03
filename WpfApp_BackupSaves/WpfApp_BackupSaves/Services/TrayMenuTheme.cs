using System.Drawing;
using System.Windows.Forms;
using BackupSaves.Core.Models;

namespace WpfApp_BackupSaves.Services;

/// <summary>WinForms tray ContextMenuStrip colors aligned with app Dark/Light themes.</summary>
internal static class TrayMenuTheme
{
    public static void Apply(ContextMenuStrip menu, AppTheme theme)
    {
        var dark = theme == AppTheme.Dark;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ToolStripProfessionalRenderer(new ColorTable(dark)) { RoundedEdges = false };
        menu.BackColor = dark ? Color.FromArgb(0x2D, 0x2D, 0x2D) : Color.White;
        menu.ForeColor = dark ? Color.FromArgb(0xE8, 0xE8, 0xE8) : Color.FromArgb(0x1A, 0x1A, 0x1A);
        menu.ShowImageMargin = false;

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripSeparator)
                continue;
            item.ForeColor = menu.ForeColor;
            item.BackColor = menu.BackColor;
        }
    }

    private sealed class ColorTable(bool dark) : ProfessionalColorTable
    {
        private readonly Color _bg = dark ? Color.FromArgb(0x2D, 0x2D, 0x2D) : Color.White;
        private readonly Color _border = dark ? Color.FromArgb(0x3F, 0x3F, 0x46) : Color.FromArgb(0xDD, 0xDD, 0xDD);
        private readonly Color _selected = dark ? Color.FromArgb(0x26, 0x4F, 0x78) : Color.FromArgb(0xCF, 0xE8, 0xFF);
        private readonly Color _pressed = dark ? Color.FromArgb(0x3C, 0x3C, 0x41) : Color.FromArgb(0xE0, 0xE0, 0xE0);

        public override Color MenuBorder => _border;
        public override Color MenuItemBorder => _selected;
        public override Color MenuItemSelected => _selected;
        public override Color MenuItemSelectedGradientBegin => _selected;
        public override Color MenuItemSelectedGradientEnd => _selected;
        public override Color MenuItemPressedGradientBegin => _pressed;
        public override Color MenuItemPressedGradientEnd => _pressed;
        public override Color MenuItemPressedGradientMiddle => _pressed;
        public override Color ToolStripDropDownBackground => _bg;
        public override Color ImageMarginGradientBegin => _bg;
        public override Color ImageMarginGradientMiddle => _bg;
        public override Color ImageMarginGradientEnd => _bg;
        public override Color SeparatorDark => _border;
        public override Color SeparatorLight => _bg;
    }
}
