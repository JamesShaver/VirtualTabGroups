using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareTreeView : TreeView
    {
        private ThemeManager _theme;

        private string _emptyStateText;
        public string EmptyStateText
        {
            get => _emptyStateText;
            set { _emptyStateText = value; Invalidate(); }
        }

        public DarkAwareTreeView()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            ShowLines = false;       // we paint our own connector lines (Task 37)
            ShowPlusMinus = false;   // we paint our own chevrons
            ShowRootLines = false;
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
            try
            {
                if (_theme == null) { base.OnDrawNode(e); return; }

                var bg = (e.State & TreeNodeStates.Selected) != 0
                    ? _theme.BackgroundHotter
                    : _theme.Background;
                using (var brush = new SolidBrush(bg))
                    e.Graphics.FillRectangle(brush, new Rectangle(0, e.Bounds.Top, Width, e.Bounds.Height));

                int indent = e.Node.Level * Indent + 2;

                if (e.Node.Level > 0)
                {
                    using (var pen = new Pen(_theme.Edge) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    {
                        int parentX = (e.Node.Level - 1) * Indent + 6;
                        int midY = e.Bounds.Top + e.Bounds.Height / 2;
                        e.Graphics.DrawLine(pen, parentX, e.Bounds.Top, parentX, midY);
                        e.Graphics.DrawLine(pen, parentX, midY, parentX + Indent, midY);
                    }
                }

                if (e.Node.Nodes.Count > 0)
                {
                    var glyphRect = new Rectangle(indent, e.Bounds.Top + (e.Bounds.Height - 8) / 2, 8, 8);
                    DrawChevron(e.Graphics, glyphRect, e.Node.IsExpanded, _theme.Text);
                }
                indent += 14;

                if (ImageList != null && e.Node.ImageIndex >= 0 && e.Node.ImageIndex < ImageList.Images.Count)
                {
                    var img = ImageList.Images[e.Node.ImageIndex];
                    e.Graphics.DrawImage(img, indent, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
                    indent += 18;
                }

                var textRect = new Rectangle(indent, e.Bounds.Top, Width - indent, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Node.Text, Font, textRect, _theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                e.DrawDefault = false;
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("DarkAwareTreeView.OnDrawNode", ex);
                e.DrawDefault = true;
            }
        }

        private static void DrawChevron(System.Drawing.Graphics g, Rectangle r, bool expanded, System.Drawing.Color color)
        {
            using (var brush = new SolidBrush(color))
            {
                if (expanded)
                {
                    var pts = new[] { new Point(r.Left, r.Top + 2), new Point(r.Right, r.Top + 2), new Point(r.Left + r.Width / 2, r.Bottom - 1) };
                    g.FillPolygon(brush, pts);
                }
                else
                {
                    var pts = new[] { new Point(r.Left + 2, r.Top), new Point(r.Left + 2, r.Bottom), new Point(r.Right - 1, r.Top + r.Height / 2) };
                    g.FillPolygon(brush, pts);
                }
            }
        }

        private Rectangle? _insertionLine;

        public void ShowInsertionLine(int y)
        {
            _insertionLine = new Rectangle(0, y, Width, 1);
            Invalidate();
        }

        public void ClearInsertionLine()
        {
            _insertionLine = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                base.OnPaint(e);
                if (_insertionLine != null && _theme != null)
                {
                    using (var pen = new Pen(_theme.HotEdge, 2))
                        e.Graphics.DrawLine(pen, _insertionLine.Value.Left, _insertionLine.Value.Top, _insertionLine.Value.Right, _insertionLine.Value.Top);
                }

                if (!string.IsNullOrEmpty(_emptyStateText) && Nodes.Count == 0 && _theme != null)
                {
                    var bounds = ClientRectangle;
                    var textRect = new Rectangle(
                        bounds.Left,
                        bounds.Top + bounds.Height / 3,
                        bounds.Width,
                        Font.Height + 4);
                    TextRenderer.DrawText(e.Graphics, _emptyStateText, Font, textRect,
                        _theme.DisabledText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("DarkAwareTreeView.OnPaint", ex);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            try
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;

                var node = GetNodeAt(e.X, e.Y);
                if (node == null || node.Nodes.Count == 0) return;

                int indent = node.Level * Indent + 2;
                var glyphRect = new Rectangle(indent, node.Bounds.Top + (node.Bounds.Height - 8) / 2, 8, 8);
                if (glyphRect.Contains(e.X, e.Y))
                {
                    if (node.IsExpanded) node.Collapse(); else node.Expand();
                }
            }
            catch (Exception ex)
            {
                CrashLog.WriteException("DarkAwareTreeView.OnMouseDown", ex);
            }
        }
    }
}
