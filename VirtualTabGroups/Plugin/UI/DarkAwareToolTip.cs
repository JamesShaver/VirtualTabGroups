using System.Drawing;
using System.Windows.Forms;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareToolTip : ToolTip
    {
        private readonly ThemeManager _theme;

        public DarkAwareToolTip(ThemeManager theme)
        {
            _theme = theme;
            OwnerDraw = true;
            Popup += OnPopup;
            Draw += OnDraw;
        }

        private void OnPopup(object sender, PopupEventArgs e)
        {
            var text = GetToolTip(e.AssociatedControl);
            var size = TextRenderer.MeasureText(text, System.Windows.Forms.Control.DefaultFont);
            e.ToolTipSize = new Size(size.Width + 12, size.Height + 8);
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            using (var bg = new SolidBrush(_theme.BackgroundDlg)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var border = new Pen(_theme.Edge)) e.Graphics.DrawRectangle(border, new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font,
                new Rectangle(6, 4, e.Bounds.Width - 12, e.Bounds.Height - 8),
                _theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
