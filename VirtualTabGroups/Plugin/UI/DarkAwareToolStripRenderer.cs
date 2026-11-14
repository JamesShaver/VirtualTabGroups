using System.Drawing;
using System.Windows.Forms;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemeManager _theme;

        public DarkAwareToolStripRenderer(ThemeManager theme) : base(new DarkColorTable(theme))
        {
            _theme = theme;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(_theme.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bg = e.Item.Selected ? _theme.SelectionBackground : _theme.Surface;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _theme.Text : _theme.DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(_theme.Edge)) e.Graphics.DrawLine(p, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(_theme.SurfaceAlt)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(_theme.Edge))
                e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1));
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            private readonly ThemeManager _t;
            public DarkColorTable(ThemeManager t) { _t = t; }
            public override Color MenuItemSelected => _t.SelectionBackground;
            public override Color MenuItemSelectedGradientBegin => _t.SelectionBackground;
            public override Color MenuItemSelectedGradientEnd => _t.SelectionBackground;
            public override Color MenuItemBorder => _t.Accent;
            public override Color MenuBorder => _t.Divider;
            public override Color ToolStripDropDownBackground => _t.Surface;
            public override Color ImageMarginGradientBegin => _t.SurfaceAlt;
            public override Color ImageMarginGradientMiddle => _t.SurfaceAlt;
            public override Color ImageMarginGradientEnd => _t.SurfaceAlt;
        }
    }
}
