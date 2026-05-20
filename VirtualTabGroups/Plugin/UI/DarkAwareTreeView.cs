using System;
using System.Drawing;
using System.Windows.Forms;
using VirtualTabGroups.Plugin;

namespace VirtualTabGroups.Plugin.UI
{
    public sealed class DarkAwareTreeView : TreeView
    {
        private ThemeManager _theme;

        // Cached GDI objects — rebuilt in ApplyTheme, disposed in DisposeCachedGdi.
        private SolidBrush _bgBrush;
        private SolidBrush _bgHotterBrush;
        private SolidBrush _textBrush;
        private Pen _edgeDottedPen;
        private Pen _hotEdgePen;
        private Brush _chevronBrush;

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

            // Double-buffer the TreeView to reduce flicker during owner-draw paint passes.
            // TreeView's DoubleBuffered is protected — set via the control-styles API.
            SetStyle(System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer
                   | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
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

            DisposeCachedGdi();
            _bgBrush        = new SolidBrush(_theme.Background);
            _bgHotterBrush  = new SolidBrush(_theme.BackgroundHotter);
            _textBrush      = new SolidBrush(_theme.Text);
            _chevronBrush   = new SolidBrush(_theme.Text);
            _edgeDottedPen  = new Pen(_theme.Edge) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            _hotEdgePen     = new Pen(_theme.HotEdge, 2);

            Invalidate();
        }

        private void DisposeCachedGdi()
        {
            _bgBrush?.Dispose();       _bgBrush       = null;
            _bgHotterBrush?.Dispose(); _bgHotterBrush = null;
            _textBrush?.Dispose();     _textBrush     = null;
            _chevronBrush?.Dispose();  _chevronBrush  = null;
            _edgeDottedPen?.Dispose(); _edgeDottedPen = null;
            _hotEdgePen?.Dispose();    _hotEdgePen    = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachTheme();
                DisposeCachedGdi();
            }
            base.Dispose(disposing);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            try
            {
                if (_theme == null) { base.OnDrawNode(e); return; }

                // Fill background using cached brushes — no per-call allocation.
                var brush = (e.State & TreeNodeStates.Selected) != 0 ? _bgHotterBrush : _bgBrush;
                e.Graphics.FillRectangle(brush, new Rectangle(0, e.Bounds.Top, Width, e.Bounds.Height));

                int indent = e.Node.Level * Indent + 2;

                // Connector lines for child nodes — uses cached dotted pen.
                if (e.Node.Level > 0 && _edgeDottedPen != null)
                {
                    int parentX = (e.Node.Level - 1) * Indent + 6;
                    int midY    = e.Bounds.Top + e.Bounds.Height / 2;
                    e.Graphics.DrawLine(_edgeDottedPen, parentX, e.Bounds.Top, parentX, midY);
                    e.Graphics.DrawLine(_edgeDottedPen, parentX, midY, parentX + Indent, midY);
                }

                if (e.Node.Nodes.Count > 0)
                {
                    var glyphRect = new Rectangle(indent, e.Bounds.Top + (e.Bounds.Height - 8) / 2, 8, 8);
                    DrawChevron(e.Graphics, glyphRect, e.Node.IsExpanded);
                }
                indent += 14;

                if (ImageList != null && e.Node.ImageIndex >= 0 && e.Node.ImageIndex < ImageList.Images.Count)
                {
                    var img = ImageList.Images[e.Node.ImageIndex];
                    e.Graphics.DrawImage(img, indent, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
                    indent += 18;
                }

                var textRect = new Rectangle(indent, e.Bounds.Top, Width - indent, e.Bounds.Height);
                // TextRenderer.DrawText accepts Color directly — no GDI allocation needed.
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

        // Draws the expand/collapse chevron using the cached chevron brush.
        private void DrawChevron(System.Drawing.Graphics g, Rectangle r, bool expanded)
        {
            if (_chevronBrush == null) return;
            if (expanded)
            {
                var pts = new[] { new Point(r.Left, r.Top + 2), new Point(r.Right, r.Top + 2), new Point(r.Left + r.Width / 2, r.Bottom - 1) };
                g.FillPolygon(_chevronBrush, pts);
            }
            else
            {
                var pts = new[] { new Point(r.Left + 2, r.Top), new Point(r.Left + 2, r.Bottom), new Point(r.Right - 1, r.Top + r.Height / 2) };
                g.FillPolygon(_chevronBrush, pts);
            }
        }

        private Rectangle? _insertionLine;

        public void ShowInsertionLine(int y)
        {
            // Skip redundant invalidates when the line position hasn't changed.
            if (_insertionLine.HasValue && _insertionLine.Value.Top == y)
                return;

            var oldY = _insertionLine?.Top;
            _insertionLine = new Rectangle(0, y, Width, 1);

            // Invalidate only the narrow strips around the new and old line positions
            // instead of the entire client area, cutting repaint work at 30-60 Hz.
            const int strip = 3;
            Invalidate(new Rectangle(0, y - strip, Width, strip * 2 + 1));
            if (oldY.HasValue)
                Invalidate(new Rectangle(0, oldY.Value - strip, Width, strip * 2 + 1));
        }

        public void ClearInsertionLine()
        {
            if (_insertionLine == null) return;
            var oldY = _insertionLine.Value.Top;
            _insertionLine = null;
            const int strip = 3;
            Invalidate(new Rectangle(0, oldY - strip, Width, strip * 2 + 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                base.OnPaint(e);
                if (_insertionLine != null && _hotEdgePen != null)
                {
                    e.Graphics.DrawLine(_hotEdgePen,
                        _insertionLine.Value.Left,  _insertionLine.Value.Top,
                        _insertionLine.Value.Right, _insertionLine.Value.Top);
                }

                if (!string.IsNullOrEmpty(_emptyStateText) && Nodes.Count == 0 && _theme != null)
                {
                    var bounds   = ClientRectangle;
                    var textRect = new Rectangle(bounds.Left, bounds.Top + bounds.Height / 3, bounds.Width, Font.Height + 4);
                    TextRenderer.DrawText(e.Graphics, _emptyStateText, Font, textRect,
                        _theme.DisabledText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            catch (Exception ex) { CrashLog.WriteException("DarkAwareTreeView.OnPaint", ex); }
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
