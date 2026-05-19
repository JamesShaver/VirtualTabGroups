using System.Drawing;
using System.Windows.Forms;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareTreeView : TreeView
    {
        private ThemeManager _theme;

        public DarkAwareTreeView()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawText;
            ShowNodeToolTips = false;
            HideSelection = false;
            LabelEdit = true;
            AllowDrop = true;
            FullRowSelect = false;
        }

        public void AttachTheme(ThemeManager theme)
        {
            _theme = theme;
            _theme.Changed += ApplyTheme;
            ApplyTheme();
        }

        public void DetachTheme()
        {
            if (_theme != null) _theme.Changed -= ApplyTheme;
            _theme = null;
        }

        private void ApplyTheme()
        {
            if (_theme == null) return;
            BackColor = _theme.Background;
            ForeColor = _theme.Text;
            LineColor = _theme.Edge;
            Invalidate();
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (_theme == null) { base.OnDrawNode(e); return; }

            var bg = (e.State & TreeNodeStates.Selected) != 0
                ? _theme.BackgroundHotter
                : _theme.Background;

            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            TextRenderer.DrawText(e.Graphics, e.Node.Text, Font, e.Bounds, _theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            e.DrawDefault = false;
        }
    }
}
